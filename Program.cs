using System;
using System.Collections.Concurrent;
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
        static readonly ConcurrentQueue<string> queue_log = new ConcurrentQueue<string>();

        static async Task Main(string[] args)
        {
            string p_url = args[0];
            int p_threads = Convert.ToInt32(args[1]);
            int p_delay = Convert.ToInt32(args[2]);
            string? p_soap_action = args.Length >= 4 ? args[3] : null;

            using var stream_reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, true);
            using var stream_error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));

            var payload = await stream_reader.ReadToEndAsync();
            var timer = Stopwatch.StartNew();
            var cts_stress = new CancellationTokenSource();
            var cts_log = new CancellationTokenSource();
            var task_stop = StressStop(cts_stress, p_delay);
            var task_log = StressLog(cts_log.Token);
            var tasks = new ConcurrentBag<Task>();
            Parallel.For(0, p_threads, x => tasks.Add(StressLoop(cts_stress.Token, p_url, payload, p_soap_action)));
            await Task.WhenAll(tasks.ToArray());
            cts_log.Cancel();
            await Task.WhenAll(task_log);
            double time = timer.ElapsedMilliseconds;
            await stream_error.WriteLineAsync($"COUNT:{counter} THREADS:{threads} TIME:{Math.Round(time / 1000, 2)}s SPEED:{Math.Round((double)counter / (time / 1000), 2)}/s");
        }

        static async Task StressLoop(CancellationToken ct, string url, string body, string? soap_action)
        {
            var registered = false;
            int myname = 0;
            while (!ct.IsCancellationRequested)
            {
                if (!registered) { myname = Interlocked.Increment(ref threads); registered = true; }
                queue_log.Enqueue($"{myname.ToString("X4")} TX {body}");
                var response = await WebServiceCall(url, body, soap_action);
                queue_log.Enqueue($"{myname.ToString("X4")} RX {response}");
                Interlocked.Increment(ref counter);
                await Task.Delay(1);
            }
        }

        static async Task StressStop(CancellationTokenSource cts, int delay)
        {
            await Task.Delay(delay * 1000);
            cts.Cancel();
        }

        static async Task StressLog(CancellationToken ct)
        {
            using var stream_writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            while ((!ct.IsCancellationRequested) || (queue_log.Count > 0))
            {
                if (queue_log.TryDequeue(out string? linha_log))
                {
                    await stream_writer.WriteLineAsync(linha_log);
                }
                else
                {
                    await Task.Delay(10);
                }
            }
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
