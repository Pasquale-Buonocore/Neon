using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Neon
{
    internal sealed class SpeedTestResult
    {
        public string Server;
        public double DownloadMbps, UploadMbps;
        public double? LatencyMs;
        public long DownloadBytes, UploadBytes;
        public TimeSpan DownloadDuration, UploadDuration;
        public DateTime FinishedAt;
    }

    internal sealed class SpeedTestProgress
    {
        public string Phase, Message;
        public double Mbps;
        public long Bytes;
    }

    internal sealed class MlabSpeedTest
    {
        private const string LocateUrl = "https://locate.measurementlab.net/v2/nearest/ndt/ndt7";
        private readonly string locateUrl;
        internal const long MaxPhaseBytes = 100L * 1024 * 1024;
        private static readonly TimeSpan MaxPhaseTime = TimeSpan.FromSeconds(5);
        internal MlabSpeedTest(string locateUrl = LocateUrl) { this.locateUrl = locateUrl; }

        internal static string TestUrl(string json, string phase, out string server)
        {
            if (phase != "download" && phase != "upload") throw new ArgumentException("Unknown test phase.");
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("results")) throw new InvalidOperationException("M-Lab returned an invalid server list.");
            var results = root["results"] as object[];
            if (results == null || results.Length == 0) throw new InvalidOperationException("M-Lab has no available test server.");
            var first = results[0] as Dictionary<string, object>;
            var urls = first != null && first.ContainsKey("urls") ? first["urls"] as Dictionary<string, object> : null;
            object value;
            if (urls == null || !urls.TryGetValue("wss:///ndt/v7/" + phase, out value) || !(value is string))
                throw new InvalidOperationException("M-Lab did not provide a secure " + phase + " endpoint.");
            var uri = new Uri((string)value);
            if (uri.Scheme != "wss" || !uri.Host.EndsWith(".measurement-lab.org", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath != "/ndt/v7/" + phase)
                throw new InvalidOperationException("M-Lab returned an unexpected test server.");
            server = uri.Host;
            return uri.AbsoluteUri;
        }

        private static void Report(IProgress<SpeedTestProgress> progress, string phase, string message, double mbps = 0, long bytes = 0)
        { progress.Report(new SpeedTestProgress { Phase = phase, Message = message, Mbps = mbps, Bytes = bytes }); }

        internal async Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress> progress, CancellationToken cancellation)
        {
            Report(progress, "finding", "Finding an M-Lab server…");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string json;
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var client = new WebClient())
            using (limit.Token.Register(client.CancelAsync))
            {
                limit.CancelAfter(TimeSpan.FromSeconds(10));
                client.Headers[HttpRequestHeader.UserAgent] = "NEON-PC-Monitor/1.0";
                client.Headers[HttpRequestHeader.Accept] = "application/json";
                try { json = await client.DownloadStringTaskAsync(new Uri(locateUrl)); }
                catch (WebException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (limit.IsCancellationRequested) throw new TimeoutException("Finding an M-Lab server timed out.");
                    throw;
                }
                catch (OperationCanceledException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (limit.IsCancellationRequested) throw new TimeoutException("Finding an M-Lab server timed out.");
                    throw;
                }
                cancellation.ThrowIfCancellationRequested();
                if (limit.IsCancellationRequested) throw new TimeoutException("Finding an M-Lab server timed out.");
            }
            cancellation.ThrowIfCancellationRequested();
            string server, uploadServer;
            var downloadUrl = TestUrl(json, "download", out server);
            var uploadUrl = TestUrl(json, "upload", out uploadServer);
            if (!String.Equals(server, uploadServer, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("M-Lab provided different servers for download and upload.");
            var result = new SpeedTestResult { Server = server };
            var download = await DownloadAsync(downloadUrl, progress, cancellation);
            result.DownloadBytes = download.Bytes; result.DownloadDuration = download.Duration;
            result.DownloadMbps = Mbps(download.Bytes, download.Duration);
            cancellation.ThrowIfCancellationRequested();
            var upload = await UploadAsync(uploadUrl, progress, cancellation);
            result.UploadBytes = upload.Bytes; result.UploadDuration = upload.Duration;
            result.UploadMbps = Mbps(upload.Bytes, upload.Duration);
            result.LatencyMs = upload.LatencyMs;
            result.FinishedAt = DateTime.Now;
            return result;
        }

        private static double Mbps(long bytes, TimeSpan duration)
        { return bytes * 8.0 / Math.Max(duration.TotalSeconds, 0.001) / 1000000; }

        private sealed class PhaseResult
        {
            public long Bytes;
            public TimeSpan Duration;
            public double? LatencyMs;
        }

        private static async Task<ClientWebSocket> ConnectAsync(string url, CancellationToken token)
        {
            var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol("net.measurementlab.ndt.v7");
            try { await socket.ConnectAsync(new Uri(url), token); return socket; }
            catch { socket.Dispose(); throw; }
        }

        private static async Task<PhaseResult> DownloadAsync(string url, IProgress<SpeedTestProgress> progress, CancellationToken cancellation)
        {
            Report(progress, "download", "Connecting for download…");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                ClientWebSocket connection;
                try { connection = await ConnectAsync(url, timeout.Token); }
                catch (OperationCanceledException) { if (!cancellation.IsCancellationRequested) throw new TimeoutException("Connecting for download timed out."); throw; }
                using (var socket = connection)
                {
                    Report(progress, "download", "Measuring download…");
                    var buffer = new byte[65536];
                    var clock = Stopwatch.StartNew();
                    long bytes = 0;
                    double lastReport = 0;
                    try
                    {
                        while (socket.State == WebSocketState.Open && clock.Elapsed < MaxPhaseTime && bytes < MaxPhaseBytes)
                        {
                            var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                            if (received.MessageType == WebSocketMessageType.Close) break;
                            if (received.MessageType != WebSocketMessageType.Binary) continue;
                            bytes += received.Count;
                            if (clock.Elapsed.TotalSeconds - lastReport >= 0.2)
                            {
                                lastReport = clock.Elapsed.TotalSeconds;
                                Report(progress, "download", "Measuring download…", Mbps(bytes, clock.Elapsed), bytes);
                            }
                        }
                    }
                    catch (OperationCanceledException) { if (!cancellation.IsCancellationRequested) throw new TimeoutException("The download timed out."); throw; }
                    clock.Stop(); cancellation.ThrowIfCancellationRequested();
                    if (bytes == 0 || clock.Elapsed.TotalSeconds < 0.5)
                        throw new InvalidOperationException("The server did not deliver enough data for a download result.");
                    Report(progress, "download-done", "Download complete.", Mbps(bytes, clock.Elapsed), bytes);
                    return new PhaseResult { Bytes = bytes, Duration = clock.Elapsed };
                }
            }
        }

        private static async Task<PhaseResult> UploadAsync(string url, IProgress<SpeedTestProgress> progress, CancellationToken cancellation)
        {
            Report(progress, "upload", "Connecting for upload…");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                ClientWebSocket connection;
                try { connection = await ConnectAsync(url, timeout.Token); }
                catch (OperationCanceledException) { if (!cancellation.IsCancellationRequested) throw new TimeoutException("Connecting for upload timed out."); throw; }
                using (var socket = connection)
                {
                    Report(progress, "upload", "Measuring upload…");
                    var buffer = new byte[8192];
                    using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buffer);
                    var receive = ReadMeasurementsAsync(socket, timeout.Token);
                    var clock = Stopwatch.StartNew();
                    long bytes = 0;
                    double lastReport = 0;
                    try
                    {
                        while (socket.State == WebSocketState.Open && clock.Elapsed < MaxPhaseTime && bytes < MaxPhaseBytes && !receive.IsCompleted)
                        {
                            await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, timeout.Token);
                            bytes += buffer.Length;
                            if (clock.Elapsed.TotalSeconds - lastReport >= 0.2)
                            {
                                lastReport = clock.Elapsed.TotalSeconds;
                                Report(progress, "upload", "Measuring upload…", Mbps(bytes, clock.Elapsed), bytes);
                            }
                        }
                    }
                    catch (OperationCanceledException) { if (!cancellation.IsCancellationRequested) throw new TimeoutException("The upload timed out."); throw; }
                    clock.Stop(); cancellation.ThrowIfCancellationRequested();
                    if (receive.IsCompleted) await receive;
                    if (bytes == 0 || clock.Elapsed.TotalSeconds < 0.5)
                        throw new InvalidOperationException("The upload ended before enough data was measured.");
                    double? latency = null;
                    try
                    {
                        if (socket.State == WebSocketState.Open)
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Done", timeout.Token);
                        if (await Task.WhenAny(receive, Task.Delay(1500, cancellation)) == receive) latency = await receive;
                    }
                    catch (WebSocketException) { }
                    catch (OperationCanceledException) { cancellation.ThrowIfCancellationRequested(); }
                    Report(progress, "upload-done", "Upload complete.", Mbps(bytes, clock.Elapsed), bytes);
                    return new PhaseResult { Bytes = bytes, Duration = clock.Elapsed, LatencyMs = latency };
                }
            }
        }

        private static async Task<double?> ReadMeasurementsAsync(ClientWebSocket socket, CancellationToken cancellation)
        {
            var buffer = new byte[65536];
            var text = new StringBuilder();
            double? latency = null;
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        if (received.CloseStatus != WebSocketCloseStatus.NormalClosure)
                            throw new InvalidOperationException("M-Lab closed upload: " + received.CloseStatus + " " + received.CloseStatusDescription);
                        break;
                    }
                    if (received.MessageType != WebSocketMessageType.Text) continue;
                    text.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
                    if (text.Length > 65536) { text.Clear(); continue; }
                    if (!received.EndOfMessage) continue;
                    var parsed = ParseLatency(text.ToString());
                    if (parsed.HasValue) latency = parsed;
                    text.Clear();
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            return latency;
        }

        internal static double? ParseLatency(string json)
        {
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                var tcp = root != null && root.ContainsKey("TCPInfo") ? root["TCPInfo"] as Dictionary<string, object> : null;
                object value;
                if (tcp == null || !tcp.TryGetValue("MinRTT", out value)) return null;
                double micros = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                return micros > 0 && micros < 10000000 ? (double?)(micros / 1000) : null;
            }
            catch (Exception) { return null; }
        }
    }
}
