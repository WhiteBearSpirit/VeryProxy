using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VeryProxy
{
    public class ProxyManager
    {
        object listLock = new object();
        object saveToFileLock = new object();
        object getNewProxiesLock = new object();

        string proxySitesFile = "proxySites.txt";
        string proxyListFile = "proxyList.txt";

        List<string> proxyList = new List<string>();
        string[] proxySites;
        int listCounter = 0;
        int sitesCounter = 0;
        int failuresInARow = 0;
        
        const int Timeout = 10000;
        const int minProxCount = 250;
        const int criticalProxCount = 100;
        const int maxFailuresInARow = 150;
        const int saveEvery = 100;
        const int reportMaxFailures = 10;
        const string AddressPortPlusRegEx = @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?):\d{1,5}[\+-]{0,10}";


        public ProxyManager(string proxyLinksFile)
        {
            proxySitesFile = proxyLinksFile;
            ReadProxySitesFromFile();
            ReadProxyListFromFile();
            if (proxyList.Count < criticalProxCount) { GetNewProxies(); }
            if (proxyList.Count < minProxCount) { Task.Run(() => GetNewProxies()); }
        }

        void ReadProxySitesFromFile()
        {
            if (!File.Exists(proxySitesFile)) { throw new InvalidOperationException($"proxySitesFile {proxySitesFile} not found!"); }
            proxySites = File.ReadAllLines(proxySitesFile).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }

        void ReadProxyListFromFile()
        {
            if (!File.Exists(proxyListFile)) { return; }
            AddProxyRangeToList(File.ReadAllLines(proxyListFile));
        }


        void AddProxyRangeToList(IEnumerable<string> listInput)
        {
            lock (listLock)
            {
                foreach (string prox in listInput)
                {
                    string matchedProx = Regex.Match(prox, AddressPortPlusRegEx).Value;
                    if (!string.IsNullOrWhiteSpace(matchedProx))
                    {
                        proxyList.Add(matchedProx);
                    }
                }
                RemoveDuplicates();
            }
        }
        void RemoveDuplicates()
        {
            List<string> processedList = new List<string>();
            List<string> currentProxiesTrimmed = new List<string>();
            foreach (string prox in proxyList)
            {
                string trimmedProx = prox.TrimEnd('+', '-', ' ');
                if (!currentProxiesTrimmed.Contains(trimmedProx))
                {
                    currentProxiesTrimmed.Add(trimmedProx);
                    processedList.Add(prox);
                }
            }
            proxyList = processedList;
        }

        public void SaveProxyListToFile()
        {
            if (Monitor.TryEnter(saveToFileLock))
            {
                try
                {
                    lock (listLock)
                    {
                        File.WriteAllLines(proxyListFile, proxyList);
                        Log.SameLine(".");
                    }
                }
                finally
                {
                    Monitor.Exit(saveToFileLock);
                }
            }
        }

        public string GiveMeProxyString()
        {
            lock (listLock)
            {
                int count = proxyList.Count;
                if (count < criticalProxCount) { Log.SameLine("[p.crit!]"); }
                if (listCounter >= count) { listCounter = 0; }
                string proxyString = proxyList[listCounter].TrimEnd('+', '-', ' ');
                listCounter++;
                if (listCounter % saveEvery == 0) { Task.Run(() => SaveProxyListToFile()); } //separate task to avoid deadlock
                return proxyString;
            }
        }
        public void Report(string proxyString, int mark)
        {
            if (proxyString == null || mark == 0) { return; }
            lock (listLock)
            {
                bool good = (mark > 0);
                failuresInARow = good ? 0 : failuresInARow + 1;
                if (failuresInARow >= maxFailuresInARow) { Log.SameLine("F"); return; }
                int index = proxyList.FindIndex(s => s.TrimEnd('+', '-', ' ') == proxyString);
                if (index == -1) { return; }
                string origString = proxyList[index];
                string postfix = "";
                for (int i = 0; i < Math.Abs(mark); i++)
                {
                    postfix = postfix + (good ? '+' : '-');
                }
                string modString = origString + postfix;
                int minusCount = modString.Count(s => s == '-');
                int plusCount = modString.Count(s => s == '+');
                if ((minusCount - plusCount) > reportMaxFailures &&
                    proxyList.Count > criticalProxCount)
                {
                    Log.SameLine("X");
                    proxyList.RemoveAt(index);
                    Task.Run(() => SaveProxyListToFile());   //separate task to avoid deadlock
                }
                else
                {
                    if ((minusCount + plusCount) > (reportMaxFailures * 2))
                    {
                        proxyList[index] = modString.TrimEnd('+', '-') + postfix;
                    }
                    else { proxyList[index] = modString; }
                }
                if (proxyList.Count < minProxCount)
                {
                    Task.Run(() => GetNewProxies());         //separate task to avoid deadlock
                }
            }
        }

        /// <summary>
        /// tries to enter getNewProxiesLock
        /// locks listLock
        /// </summary>
        void GetNewProxies()
        {
            if (Monitor.TryEnter(getNewProxiesLock))
            {
                try
                {
                    lock (listLock)
                    {
                        if (proxyList.Count >= minProxCount) { return; }
                    }
                    List<string> newProxies = new List<string>();
                    for (int i = 0; i < proxySites.Length; i++)
                    {
                        newProxies.AddRange(GetProxyListFromNextSite());
                        if (newProxies.Count > criticalProxCount) { break; }
                    }
                    if (newProxies.Count == 0) { throw new OutOfProxyException(); }
                    AddProxyRangeToList(newProxies);
                    SaveProxyListToFile();
                }
                finally
                {
                    Monitor.Exit(getNewProxiesLock);
                }
            }   
        }


        List<string> GetProxyListFromNextSite()
        {
            var proxList = new List<string>();
            try
            {
                if (proxySites.Length == 0) { return proxList; }
                if (sitesCounter >= proxySites.Length) { sitesCounter = 0; }
                string site = proxySites[sitesCounter];
                sitesCounter++;
                Log.Line($"[PM: Getting proxies from {site}] ");
                var result = ProcessProxyURL(site, 5);
                Log.Line($"[PM: Got page content...] ");
                if (string.IsNullOrWhiteSpace(result)) { Log.SameLine("[PM: content is empty] "); return proxList; }
                proxList = ParseProxyPage(result);
                int count = proxList.Count;
                Log.Line($"[PM: Got {count} proxies] ");
            }
            catch { }
            return proxList;
        }

        string ProcessProxyURL(string url, int retryCount)
        {
            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Credentials = CredentialCache.DefaultCredentials;
                    request.Timeout = Timeout;
                    request.ReadWriteTimeout = Timeout;
                    request.KeepAlive = false;

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                            {
                                string data = reader.ReadToEnd();
                                reader.Close();
                                response.Close();
                                return data;
                            }
                        }
                    }
                }
                catch { }
            }
            Log.Line($"[PM: Task failed after {retryCount} reties] ");
            return null;
        }

        List<string> ParseProxyPage(string page)
        {
            //5.2.75.170:1080 :
            const string addressPortRegEx =
                @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?):\d{1,5}\b";
            //5.2.75.170</td><td>1080</td> :
            const string tdProxyRegex =
                @"\b((?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?))</td><td>(\d{1,5})\b";
            //<td>124.88.67.54</td><td><a href="/proxylist/port/81">81</a>:
            const string ahrefProxyRegex =
                @"\b((?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?))</td><td><a.*?>(\d{1,5})\b";
            const string gatherProxyRegex =
                "PROXY_IP\":\"((?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?))\",\"PROXY_LAST_UPDATE\":\"(?:[\\d\\s]+)\",\"PROXY_PORT\":\"([\\dA-F]{1,4})\\b";
            const string proxynovaRegex =
                @"((?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?))\s*(?:<.*?>\s*){1,3}(\d{1,5})";
            List<string> proxList = new List<string>();
            foreach (Match match in Regex.Matches(page, addressPortRegEx))
            {
                string matchedProx = match.Value;
                if (!string.IsNullOrWhiteSpace(matchedProx))
                {
                    proxList.Add(matchedProx);
                }
            }
            foreach (Match match in Regex.Matches(page, tdProxyRegex))
            {
                if (match.Groups.Count < 3) { continue; }
                string address = match.Groups[1].Value;
                string port = match.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(port))
                {
                    proxList.Add(address + ":" + port);
                }
            }
            foreach (Match match in Regex.Matches(page, ahrefProxyRegex))
            {
                if (match.Groups.Count < 3) { continue; }
                string address = match.Groups[1].Value;
                string port = match.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(port))
                {
                    proxList.Add(address + ":" + port);
                }
            }
            foreach (Match match in Regex.Matches(page, gatherProxyRegex))
            {
                if (match.Groups.Count < 3) { continue; }
                string address = match.Groups[1].Value;
                string hexPort = match.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(hexPort))
                {
                    int port = Convert.ToInt32(hexPort, 16);
                    proxList.Add(address + ":" + port);
                }
            }
            string pageWithoutSpan = page.Replace("</span>", "").Replace("<span>", "");
            foreach (Match match in Regex.Matches(pageWithoutSpan, proxynovaRegex))
            {
                if (match.Groups.Count < 3) { continue; }
                string address = match.Groups[1].Value;
                string port = match.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(port))
                {
                    proxList.Add(address + ":" + port);
                }
            }
            return proxList;
        }
    }




    public class OutOfProxyException : Exception
    {
        public OutOfProxyException() { }
        public OutOfProxyException(string message) : base(message) { }
    }
}