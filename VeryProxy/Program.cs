using System;
using System.Collections.Concurrent;
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
        const int maxDegreeOfParallelism = 200;
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
            ProxyManager proxx = new ProxyManager(proxySitesFile);
            Directory.CreateDirectory(outputDir);
            ParallelOptions p = new ParallelOptions() { MaxDegreeOfParallelism = maxDegreeOfParallelism };
            Parallel.ForEach(urlsFromFile, p, url => 
            {
                string page = ProcessURL_string(url, proxx, maxRetries);
                if (string.IsNullOrWhiteSpace(page))
                {
                    Log.Line($"Content from {url} is empty!");
                }
                else
                {
                    string filename = MakeFileName(url);
                    try
                    {
                        File.WriteAllText(Path.Combine(outputDir, filename), page);
                        Log.Line("Saved " + filename);
                    }
                    catch { Log.Line("Failed to save " + filename); }
                }
            });
            proxx.SaveProxyListToFile();
            Log.Line("Complete!");
            Console.ReadLine();
        }


        static string MakeFileName(string name)
        {            
            string str = Regex.Replace(name, "^//|^.*?://^//|^.*?://", ""); ;
            if (string.IsNullOrWhiteSpace(str)) { str = "_"; }
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()))+'.';
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            string escaped = Regex.Replace(str, invalidRegStr, "_");       
            if (escaped.Length > 180)
            {
                escaped = escaped.Substring(0, 30) + "(__)"+ escaped.Substring(escaped.Length - 140, 140);
            }
            return escaped + ".html";
        }


        static string ProcessURL_string(string url, ProxyManager proxx, int retryCount = 30)
        {
            if (proxx == null) { throw new ArgumentNullException(); }
            for (int i = 0; i < retryCount; i++)
            {
                //decreasing timeout up to half with every next retry
                int myTimeout = (int)(Program.Timeout.TotalMilliseconds * (retryCount - (i / 2)) / retryCount);
                string proxyString = proxx.GiveMeProxyString();
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Proxy = new WebProxy(proxyString);
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
                                if (seconds < Timeout.TotalSeconds) { proxx.Report(proxyString, 2); }
                                else { proxx.Report(proxyString, 1); }
                                Log.SameLine("+");
                                return data;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    int mark = (sw.Elapsed.TotalSeconds > (Timeout.TotalSeconds * 4)) ? -2 : -1;
                    if (ex is WebException ||
                        ex is IOException)
                    {
                        proxx.Report(proxyString, mark);
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

        static Tuple<string, string> ProcessURL(string url, ProxyManager proxx, int retryCount = 30)
        {
            if (proxx == null) { throw new ArgumentNullException(); }
            for (int i = 0; i < retryCount; i++)
            {
                //decreasing timeout up to half with every next retry
                int myTimeout = (int)(Program.Timeout.TotalMilliseconds * (retryCount - (i / 2)) / retryCount);
                string proxyString = proxx.GiveMeProxyString();
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Proxy = new WebProxy(proxyString);
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
                                if (seconds < Timeout.TotalSeconds) { proxx.Report(proxyString, 2); }
                                else { proxx.Report(proxyString, 1); }
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
                        proxx.Report(proxyString, mark);
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


        static async Task<Tuple<string, string>> ProcessURLAsync_httpclient(string url, ProxyManager proxx, int retryCount = 30)
        {
            if (proxx == null) { throw new ArgumentNullException(); }

            for (int i = 0; i < retryCount; i++)
            {
                string proxyString = proxx.GiveMeProxyString();
                var handler = new HttpClientHandler
                {
                    UseDefaultCredentials = false,
                    Proxy = new WebProxy(proxyString),
                    UseProxy = true,
                };
                HttpClient myHttpClient = new HttpClient(handler) { Timeout = Timeout };
                try
                {
                    var responseMessage = await myHttpClient.GetAsync(url);
                    if (responseMessage.StatusCode == HttpStatusCode.OK)
                    {
                        string data = await responseMessage.Content.ReadAsStringAsync();
                        proxx.Report(proxyString, 1);
                        Log.SameLine("+");
                        return Tuple.Create(url, data);
                    }
                    else
                    {
                        Log.SameLine($"[{(int)responseMessage.StatusCode}]");
                    }
                }
                catch (Exception ex)
                {
                    if (ex is System.Net.Http.HttpRequestException 
                        || ex is WebException 
                        || ex is TaskCanceledException
                        || ex is System.IO.IOException)
                    {
                        proxx.Report(proxyString, -1);
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


        static async Task<Tuple<string, string>> ProcessURLAsync_webclient(string url, ProxyManager proxx, int retryCount = 30)
        {
            if (proxx == null) { throw new ArgumentNullException(); }

            for (int i = 0; i < retryCount; i++)
            {
                string proxyString = proxx.GiveMeProxyString();
                var handler = new HttpClientHandler
                {
                    UseDefaultCredentials = false,
                    Proxy = new WebProxy(proxyString),
                    UseProxy = true,
                };
                WebClient client = new WebClient();
                client.Proxy = new WebProxy(proxyString);
                
                try
                {
                    var response = await client.DownloadStringTaskAsync(url);
                    proxx.Report(proxyString, 1);
                    Log.SameLine("+");
                    return Tuple.Create(url, response);
                }
                catch (Exception ex)
                {
                    if (ex is System.Net.Http.HttpRequestException
                        || ex is WebException
                        || ex is TaskCanceledException
                        || ex is System.IO.IOException)
                    {
                        proxx.Report(proxyString, -1);
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