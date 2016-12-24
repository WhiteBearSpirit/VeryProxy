using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VeryProxy
{
    public class ProxyManager
    {
        //Todo: if 20 failures in a row then 'network/target_site failure', don't mark proxies as bad until first good report
        object listLock = new object();
        object saveToFileLock = new object();

        List<string> proxyStrings = new List<string>();
        int listCounter = 0;

        const string pp = "proxy.txt";
        const int criticalProxCount = 5;
        const int saveEvery = 10;
        const int maxFailures = 5;

        public ProxyManager(string proxyFilePath = pp)
        {
            ReadProxyListFromFile();
        }

        public void ReadProxyListFromFile(string path = pp)
        {
            //uses lock (listLock) inside
            AddProxyRangeToList(File.ReadAllLines(path));
        }
        void AddProxyRangeToList(IEnumerable<string> listInput)
        {
            lock (listLock)
            {
                foreach (string prox in listInput)
                {
                    string matchedProx = Regex.Match(prox, Utils.AddressPortPlusRegEx).Value;
                    if (matchedProx != null)
                    {
                        proxyStrings.Add(matchedProx);
                    }
                }
                RemoveDuplicates();
            }
        }
        void RemoveDuplicates()
        {
            List<string> processedList = new List<string>();
            List<string> currentProxiesTrimmed = new List<string>();
            foreach (string prox in proxyStrings)
            {
                string trimmedProx = prox.TrimEnd('+', '-', ' ');
                if (!currentProxiesTrimmed.Contains(trimmedProx))
                {
                    currentProxiesTrimmed.Add(trimmedProx);
                    processedList.Add(prox);
                }
            }
            proxyStrings = processedList;
        }

        public void SaveProxyListToFile(string path = pp)
        {
            if (Monitor.TryEnter(saveToFileLock))
            {
                try
                {
                    lock (listLock)
                    {
                        File.WriteAllLines(path, proxyStrings);
                    }
                }
                finally
                {
                    Monitor.Exit(saveToFileLock);
                }
            }
        }

        public string GiveNewProxyString()
        {
            lock (listLock)
            {
                if (proxyStrings.Count == 0)
                {
                    throw new OutOfProxyException();
                }
                if (listCounter >= proxyStrings.Count)
                {
                    listCounter = 0;
                }
                string proxyString = proxyStrings[listCounter].TrimEnd('+', '-', ' ');
                listCounter++;
                if (listCounter % saveEvery == 0)
                {
                    Task.Run(() => SaveProxyListToFile());
                }
                return proxyString;
            }
        }
        public void Report(string proxyString, bool good)
        {
            if (proxyString == null) { return; }
            lock (listLock)
            {
                int index = proxyStrings.FindIndex(s => s.Contains(proxyString));
                if (index == -1)
                {
                    return;
                }
                string origString = proxyStrings[index];
                string modString = origString + (good ? '+' : '-');
                int minusCount = modString.Count(s => s == '-');
                int plusCount = modString.Count(s => s == '+');
                if ((minusCount - plusCount) > maxFailures &&
                    proxyStrings.Count > criticalProxCount)
                {
                    Log.SameLine("X");
                    proxyStrings.RemoveAt(index);
                    Task.Run(() => SaveProxyListToFile());
                }
                else
                {
                    Log.SameLine(good ? "+" : "-");
                    if ((minusCount + plusCount) > (maxFailures * 2))
                    {
                        proxyStrings[index] = modString.TrimEnd('+', '-') + (good ? '+' : '-');
                    }
                    else { proxyStrings[index] = modString; }
                }

            }
        }


    }

}
