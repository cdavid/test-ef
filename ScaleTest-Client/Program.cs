using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    internal static class Program
    {
        private static HttpClient _httpClient = new HttpClient();
        private static string _brokerBaseUrl = "http://localhost:54580";
        private static int _itemCount = 10;
        private static Stopwatch _stopwatch = new Stopwatch();

        public static async Task Main(string[] args)
        {
            if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
            {
                _brokerBaseUrl = args[0];
            } 

            if (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
            {
                _itemCount = int.Parse(args[1]);
            }

            _httpClient.BaseAddress = new Uri(_brokerBaseUrl);
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
            ServicePointManager.DefaultConnectionLimit = 10000;

            long lastTime = 0;

            Log("Begin test run with arguments: Url={0} , Count={1}", _brokerBaseUrl, _itemCount);
            _stopwatch.Start();

            await DoPing().ConfigureAwait(false);
            Log($"Ping OK, took {_stopwatch.ElapsedMilliseconds - lastTime}ms");
            lastTime = _stopwatch.ElapsedMilliseconds;

            var items = await GetItems(_itemCount).ConfigureAwait(false);
            Log($"Fetch OK, took {_stopwatch.ElapsedMilliseconds - lastTime}ms");
            lastTime = _stopwatch.ElapsedMilliseconds;

            await UpdateItems(items);
            Log($"Update OK, took {_stopwatch.ElapsedMilliseconds - lastTime}ms");
            lastTime = _stopwatch.ElapsedMilliseconds;

            await GetResult();
            Log($"Get Result OK, took {_stopwatch.ElapsedMilliseconds - lastTime}ms");
            lastTime = _stopwatch.ElapsedMilliseconds;

            //Console.ReadKey();
        }

        private static void Log(string format, params object[] args)
        {
            Console.WriteLine(format, args);
        }

        const string pingUrl = "/test/Ping";
        const string populateUrl = "/test/PopulateDatabaseAsync?count={0}";
        const string getUrl = "/test/GetHitListAsync";
        const string updateUrl = "/test/UpdateItemAsync/{0}";
        const string cleanUrl = "/test/CleanDatabaseAsync";
        const string profileUrl = "/test/GetProfilingDataSinceLastPopulateAsync";

        // 1. ping to make sure everything is fine
        private static async Task DoPing()
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, pingUrl))
            {
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var output = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (string.Equals("pong", output))
                    {
                        return;
                    }
                }
                throw new InvalidOperationException("Controller not started");
            }
        }

        // 2. populate database with N Hits and get the list of ids
        private static async Task<List<Guid>> GetItems(int itemCount)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, string.Format(populateUrl, itemCount)))
            {
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var output = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || !string.Equals(itemCount.ToString(), output))
                {
                    throw new InvalidOperationException("Database populate failed");
                }
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, getUrl))
            {
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var output = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var list = JsonConvert.DeserializeObject<List<Guid>>(output);
                    if (list.Count < itemCount)
                    {
                        throw new InvalidOperationException($"Wrong number of items on the server side: {list.Count}");
                    }
                    return list.GetRange(0, itemCount);
                }
                throw new InvalidOperationException("Response from server was invalid");
            }
        }

        // 3. Make N requests
        private static async Task UpdateItems(List<Guid> items)
        {
            var tasks = items.Select(async item =>
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, string.Format(updateUrl, item)))
                {
                    var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    var output = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        if (!item.Equals(Guid.Parse(output.Replace("\"", ""))))
                        {
                            throw new InvalidOperationException($"Invalid response in request {item}");
                        }
                        return;
                    }
                    throw new InvalidOperationException($"Exception in task {item}: {output}");
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // 4. Clean database
        private static async Task CleanDatabase()
        {
            await Task.CompletedTask;
        }

        // 5. Download results
        private static async Task GetResult()
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, profileUrl))
            {
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var output = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                    Log($"Writing results to {filePath}");
                    await File.WriteAllTextAsync(filePath, output).ConfigureAwait(false);
                }
            }
        }
    }
}
