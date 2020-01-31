using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace stresser
{
    class Program
    {
        static int counter = 0;
        static int threads = 0;
        static readonly HttpClient http_client = new HttpClient();

        static async Task Main(string[] args)
        {
            string p_url = args[0];
            int p_threads = Convert.ToInt32(args[1]);
            int p_delay = Convert.ToInt32(args[2]);
            string? p_soap_action = args.Length >= 4 ? args[3] : null;

            using var stream_reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, true);
            var payload = await stream_reader.ReadToEndAsync();
            var timer = Stopwatch.StartNew();
            var cts = new CancellationTokenSource();
            var task_stop = StressStop(cts, p_delay);
            var tasks = new List<Task>(p_threads);
            Parallel.For(0, p_threads, x => tasks.Add(StressLoop(cts.Token, p_url, payload, p_soap_action)));
            Task.WaitAll(tasks.ToArray());
            double time = timer.ElapsedMilliseconds;
            Console.WriteLine($"COUNT:{counter} THREADS:{threads} TIME:{Math.Round(time / 1000, 2)}s SPEED:{Math.Round((double)counter / (time / 1000), 2)}/s");
        }

        static async Task StressLoop(CancellationToken ct, string url, string body, string? soap_action)
        {
            var registered = false;
            while (!ct.IsCancellationRequested)
            {
                if (!registered) { Interlocked.Increment(ref threads); registered = true; }
                await WebServiceCall(url, body, soap_action);
                Interlocked.Increment(ref counter);
                await Task.Delay(1);
            }
        }

        static async Task StressStop(CancellationTokenSource cts, int delay)
        {
            Console.WriteLine("START!");
            await Task.Delay(delay * 1000);
            Console.WriteLine("STOP!");
            cts.Cancel();
        }

        static async Task<string> WebServiceCall(string str_url, string str_body, string? soap_action = null)
        {
            try
            {
                var request_bytes = Encoding.UTF8.GetBytes(str_body);
                var http_content = new ByteArrayContent(request_bytes, 0, request_bytes.Length);

                if (soap_action != null)
                {
                    http_content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
                    http_content.Headers.Add("SOAPAction", soap_action);
                }
                else
                {
                    http_content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                }

                http_content.Headers.ContentType.CharSet = "utf-8";
                var http_response = await http_client.PostAsync(str_url, http_content);
                http_response.EnsureSuccessStatusCode();
                return await http_response.Content.ReadAsStringAsync();
            }
            catch (Exception exception)
            {
                throw new Exception(nameof(WebServiceCall), exception);
            }
        }
    }
}
