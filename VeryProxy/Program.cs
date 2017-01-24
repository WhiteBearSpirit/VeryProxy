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
        const string outputDir = "out";

        static void Main(string[] args)
        {
            //magic
            const int limit = 1000;
            System.Threading.ThreadPool.SetMinThreads(limit, limit);
            ServicePointManager.DefaultConnectionLimit = limit;
            ServicePointManager.MaxServicePointIdleTime = 1000;
            ServicePointManager.Expect100Continue = false;

            Log.SameLine("Welcome to Very Proxy!");
            if (args.Length < 2)
            {
                Log.Line("Not enough arguments provided (should be 2)");
                Log.Line("Usage: VeryProxy.exe PROXY_LINKS.txt LINKS_TO_DOWNLOAD.txt");
                return;
            }
            string proxySitesFile = args[0];
            if (!File.Exists(proxySitesFile)) { Log.Line("No such file: "+ proxySitesFile); return; }
            string urlFile = args[1];
            if (!File.Exists(urlFile)) { Log.Line("No such file: " + urlFile); return; }
            string[] urlsFromFile = File.ReadAllLines(urlFile);
            Directory.CreateDirectory(outputDir);
            ProxyManager proxx = new ProxyManager(proxySitesFile);
            List<Task> taskList = new List<Task>();
            foreach (string url in urlsFromFile)
            {
                if (string.IsNullOrWhiteSpace(url)) { continue; }
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
                File.WriteAllText(Path.Combine(outputDir, filename), page);
                Log.Line("Saved " + filename);
            }
            proxx.SaveProxyListToFile();
            Console.ReadLine();
        }


        static string MakeFileName(string name)
        {
            string str = name;
            if (string.IsNullOrWhiteSpace(str)) { str = "_"; }
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()))+'.';
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            string escaped = Regex.Replace(str, invalidRegStr, "_");       
            if (escaped.Length > 250) { escaped = escaped.Substring(0, 250); }
            return escaped + ".html";
        }


        static Tuple<string, string> ProcessURL(string url, ProxyManager proxx, int retryCount = 30)
        {
            for (int i = 0; i < retryCount; i++)
            {
                //decreasing timeout up to half with every next retry
                int myTimeout = (int)(Program.Timeout.TotalMilliseconds * (retryCount - (i / 2)) / retryCount);
                bool useProxy = (proxx != null);
                string proxyString = (useProxy) ? proxx.GiveMeProxyString() : null;
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    if (useProxy) { request.Proxy = new WebProxy(proxyString); }
                    request.Credentials = CredentialCache.DefaultCredentials;
                    request.Timeout = myTimeout;
                    request.ReadWriteTimeout = myTimeout;
                    request.KeepAlive = false;
                    sw.Start();
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                            {
                                string data = reader.ReadToEnd();
                                reader.Close();
                                response.Close();
                                sw.Stop();
                                double seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
                                if (useProxy)
                                {
                                    if (seconds < Timeout.TotalSeconds) { proxx.Report(proxyString, 2); }
                                    else { proxx.Report(proxyString, 1); }
                                }
                                Log.SameLine("+");
                                return Tuple.Create(url, data);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    int mark = (sw.Elapsed.TotalSeconds > (Timeout.TotalSeconds * 4)) ? -2 : -1;
                    if (ex is WebException ||
                        ex is System.IO.IOException)
                    {
                        if (useProxy) { proxx.Report(proxyString, mark); }
                        Log.SameLine("-");
                    }
                    else
                    {
                        Log.SameLine("?");
                        Log.Data(ex);
                    }
                }
            }
            Log.Line($"[Task failed after {retryCount} reties] ");
            return null;
        }
    }
}