using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VeryProxy
{
    class Program
    {
        static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        const int maxRetries = 25;

        static void Main(string[] args)
        {
            //magic
            const int limit = 200;
            System.Threading.ThreadPool.SetMinThreads(limit, limit);
            ServicePointManager.DefaultConnectionLimit = limit;
            ServicePointManager.MaxServicePointIdleTime = 1000;
            ServicePointManager.Expect100Continue = false;

            Log.SameLine("Welcome to Very Proxy!");
            if (args.Length < 2)
            {
                Log.Line("Not enough arguments provided (should be 2)");
                Log.Line("Usage: VeryProxy.exe FILE_WITH_PROXY_SITES.txt FILE_WITH_URLS_TO_DOWNLOAD.txt");
                return;
            }
            string proxySitesFile = args[0];
            if (!File.Exists(proxySitesFile)) { Log.Line("No such file: "+ proxySitesFile); return; }
            string[] proxySites = File.ReadAllLines(proxySitesFile);
            string urlFile = args[1];
            if (!File.Exists(urlFile)) { Log.Line("No such file: " + urlFile); return; }
            string[] urlsFromFile = File.ReadAllLines(urlFile);

            var proxList = new List<string>();
            foreach (string site in proxySites)
            {
                Log.Line($"Getting proxies from {site} ");
                var result = ProcessURL(site, null, maxRetries);
                if (result == null || string.IsNullOrWhiteSpace(result.Item2)) { continue; }
                string proxyPage = result.Item2;
                var list = ParseProxyPage(proxyPage);
                int count = list.Count;
                Log.SameLine($" ({count} proxies) ");
                proxList.AddRange(list);
            }
            if (proxList.Count == 0) { Log.Line("Couldn't get any proxy from proxy sites!"); return; }

            ProxyManager proxx = new ProxyManager(proxList);

            List<Task> taskList = new List<Task>();
            foreach (string url in urlsFromFile)
            {
                taskList.Add(Task.Run(() => { return ProcessURL(url, proxx, maxRetries); }));
            }
            Log.Line($"Started tasks: {taskList.Count}. ");
            Task.WaitAll(taskList.ToArray());
            Log.Line("Done!");
            foreach (Task<Tuple<string, string>> task in taskList)
            {
                if (task.Result == null) { continue; }
                string url = task.Result.Item1;
                string page = task.Result.Item2;
                if (string.IsNullOrWhiteSpace(page)) { Log.Line($"Content from {url} is empty!"); continue; }
                string filename = MakeFileName(url);
                File.WriteAllText(filename, page);
                Log.Line("Saved " + filename);
            }
            proxx.SaveProxyListToFile();
            Console.ReadLine();
        }

        static List<string> ParseProxyPage(string page)
        {
            //5.2.75.170:1080 :
            const string AddressPortRegEx = 
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
            foreach (Match match in Regex.Matches(page, AddressPortRegEx))
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
                    proxList.Add(address+":"+ port);
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

        static string MakeFileName(string name)
        {
            string str = name;
            if (string.IsNullOrWhiteSpace(str)) { str = "_"; }
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()))+'.';
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            string escaped = Regex.Replace(str, invalidRegStr, "_");            
            return escaped + ".html";
        }


        static Tuple<string, string> ProcessURL(string url, ProxyManager proxx = null, int retryCount = 10)
        {
            for (int i = 0; i < retryCount; i++)
            {
                //decreasing timeout up to half with every next retry
                int myTimeout = (int)(Program.Timeout.TotalMilliseconds * (retryCount - (i / 2)) / retryCount);
                bool useProxy = (proxx != null);
                string proxyString = (useProxy) ? proxx.GiveNewProxyString() : null;
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    if (useProxy) { request.Proxy = new WebProxy(proxyString); }
                    request.Credentials = CredentialCache.DefaultCredentials;
                    request.Timeout = myTimeout;
                    request.ReadWriteTimeout = myTimeout;
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
                                if (proxx != null) { proxx.Report(proxyString, true); }
                                Log.SameLine("+");
                                return Tuple.Create(url, data);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (ex is WebException ||
                        ex is System.IO.IOException)
                    {
                        if (proxx != null) { proxx.Report(proxyString, false); }
                        Log.SameLine("-");
                    }
                    else
                    {
                        Log.SameLine("?");
                        Log.Data(ex);
                    }
                }
            }
            Log.Line($"Task failed after {retryCount} reties");
            return null;
        }
    }
}