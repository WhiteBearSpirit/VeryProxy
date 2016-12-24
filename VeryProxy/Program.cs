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
        static void Main(string[] args)
        {
            const int maxRetries = 10;
            Log.SameLine("Welcome to Very Proxy!");
            if (args.Length < 2)
            {
                Log.Line("Not enough arguments provided (should be at least 2)");
                Log.Line("Usage: VeryProxy.exe FILE_WITH_PROXIES.txt FILE_WITH_URLS.txt");
                return;
            }
            ProxyManager proxx = new ProxyManager(args[0]);
            string urlFile = args[1];

            List<string> listOfPages = new List<string>();
            List<Task> taskList = new List<Task>();
            foreach (string url in File.ReadAllLines(urlFile))
            {
                taskList.Add(ProcessURLAsync(proxx, url, maxRetries));
            }
            Log.Line($"Started tasks: {taskList.Count}. Progress: ");
            Task.WaitAll(taskList.ToArray());
            proxx.SaveProxyListToFile();
            Log.Line("Done!");
            foreach (Task<Tuple<string, string>> task in taskList)
            {
                string url = task.Result.Item1;
                string page = task.Result.Item2;
                string filename = MakeFileName(url);
                File.WriteAllText(filename, page);
                Log.Line("Saved " + filename);
            }
            Console.ReadLine();
        }

        static string MakeFileName(string name)
        {
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()))+'.';
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

            string escaped = Regex.Replace(name, invalidRegStr, "_");
            return escaped + ".html";
        }


        static async Task<Tuple<string, string>> ProcessURLAsync(ProxyManager proxx, string url, int retryCount)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(20);

            for (int i = 0; i < retryCount; i++)
            {
                string proxyString = proxx.GiveNewProxyString();
                HttpClientHandler aHandler =
                    new HttpClientHandler() { Proxy = new WebProxy(proxyString), UseProxy = true };
                HttpClient client = new HttpClient(aHandler) { Timeout = timeout };
                try
                {
                    HttpResponseMessage x = await client.GetAsync(url).TimeoutAfter(timeout);
                    if (x.StatusCode == HttpStatusCode.OK)
                    {
                        string data = await x.Content.ReadAsStringAsync().TimeoutAfter(timeout);
                        proxx.Report(proxyString, true);
                        return Tuple.Create(url, data);
                    }
                    else
                    {
                        proxx.Report(proxyString, false);
                    }
                }
                catch (Exception ex)
                {
                    if (ex is HttpRequestException ||
                        ex is WebException ||           //for MONO
                        ex is System.IO.IOException ||  //for MONO
                        ex is TaskCanceledException ||  //if timeout
                        ex is TimeoutException)         //if timeout
                    {
                        proxx.Report(proxyString, false);
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