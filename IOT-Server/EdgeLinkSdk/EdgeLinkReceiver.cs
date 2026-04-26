using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EdgeLink
{
    public enum Protocol { TCP, UDP }

    /// <summary>
    /// EdgeLink 統一接收器，支援 TCP 與 UDP。
    /// 在 MonoBehaviour.Update() 裡呼叫 Flush()，事件才會在 Unity 主執行緒觸發。
    /// </summary>
    public class EdgeLinkReceiver : IDisposable
    {
        // ── 事件 ───────────────────────────────────────────────────────────────

        /// <summary>每筆新訊息到達時觸發。</summary>
        public event Action<IotMessage> OnMessage;

        /// <summary>連線狀態變更時觸發（TCP 才有，UDP 不適用）。</summary>
        public event Action<bool> OnConnectionChanged;

        /// <summary>
        /// IoT 設備連線/斷線時觸發。
        /// 第一個參數為 EdgeLink 上設定的 Protocol Name，
        /// 第二個參數為設備的 IP:Port（例如 "192.168.1.101:5000"），
        /// 第三個參數為連線狀態。
        /// </summary>
        public event Action<string, string, bool> OnDeviceStatusChanged;

        /// <summary>發生錯誤時觸發。</summary>
        public event Action<Exception> OnError;

        // ── 公開資料 ───────────────────────────────────────────────────────────

        /// <summary>最後一筆收到的訊息，可直接在 Update() 讀取。</summary>
        public IotMessage LatestMessage { get; private set; }

        /// <summary>目前是否正在監聽。</summary>
        public bool IsListening { get; private set; }

        /// <summary>使用的通訊協定。</summary>
        public Protocol Protocol { get; }

        // ── 內部欄位 ───────────────────────────────────────────────────────────

        private readonly MaskParser parser;
        private TcpListener tcpListener;
        private UdpClient udpClient;
        private CancellationTokenSource cancelSource;
        private readonly ConcurrentQueue<Action> pendingEvents = new ConcurrentQueue<Action>();
        private readonly ConcurrentDictionary<string, StreamEntry> streams = new ConcurrentDictionary<string, StreamEntry>();

        private sealed class StreamEntry
        {
            public readonly NetworkStream Stream;
            public readonly SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);
            public StreamEntry(NetworkStream stream) => Stream = stream;
        }

        /// <param name="protocol">選擇 TCP 或 UDP。</param>
        /// <param name="definition">
        /// 韌體工程師提供的遮罩定義（選填）。
        /// 傳入後可用 ParseOutput() 從收到的字串反向提取欄位值。
        /// </param>
        public EdgeLinkReceiver(Protocol protocol, MaskDefinition definition = null)
        {
            Protocol = protocol;
            parser   = definition != null ? new MaskParser(definition) : null;
        }

        /// <summary>
        /// 開始監聽指定 port。
        /// IoT Server 的目標 IP 和 Port 要對應到這台機器的這個 port。
        /// </summary>
        public void Start(int port)
        {
            if (cancelSource != null)
                throw new InvalidOperationException("已啟動，請先呼叫 Stop()。");

            cancelSource = new CancellationTokenSource();
            IsListening  = true;

            if (Protocol == Protocol.TCP)
            {
                tcpListener = new TcpListener(IPAddress.Any, port);
                tcpListener.Start();
                Task.Run(() => TcpAcceptLoop(cancelSource.Token));
            }
            else
            {
                udpClient = new UdpClient(port);
                Task.Run(() => UdpReceiveLoop(cancelSource.Token));
            }
        }

        /// <summary>停止監聽。</summary>
        public void Stop()
        {
            cancelSource?.Cancel();
            tcpListener?.Stop();
            tcpListener?.Server?.Close();
            udpClient?.Close();
            cancelSource = null;
            tcpListener  = null;
            udpClient    = null;
            IsListening  = false;
            streams.Clear();
        }

        /// <summary>在 MonoBehaviour.Update() 呼叫，讓事件在主執行緒觸發。</summary>
        public void Flush()
        {
            while (pendingEvents.TryDequeue(out var action))
            {
                try { action(); } catch { }
            }
        }

        /// <summary>
        /// 發送訊息給所有已連線的 EdgeLink Server（TCP 限定）。
        /// 可用於雙向通訊，例如發送指令給 IoT 設備。
        /// </summary>
        public async Task SendAsync(string message)
        {
            if (Protocol != Protocol.TCP || string.IsNullOrEmpty(message)) return;

            byte[] bytes = Encoding.UTF8.GetBytes(message.EndsWith("\n") ? message : message + "\n");
            foreach (var kv in streams)
                await WriteAsync(kv.Value, bytes);
        }

        public void Dispose() => Stop();

        // ── TCP ────────────────────────────────────────────────────────────────

        private async Task TcpAcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await tcpListener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    _ = Task.Run(() => TcpReadLoop(client, token));
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { Schedule(() => OnError?.Invoke(ex)); }
            }
        }

        private async Task TcpReadLoop(TcpClient client, CancellationToken token)
        {
            string key   = Guid.NewGuid().ToString("N");
            var entry    = new StreamEntry(client.GetStream());
            streams[key] = entry;
            Schedule(() => OnConnectionChanged?.Invoke(true));

            try
            {
                using (client)
                using (var reader = new StreamReader(entry.Stream, Encoding.UTF8))
                {
                    string line;
                    while (!token.IsCancellationRequested &&
                           (line = await reader.ReadLineAsync()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            await DeliverAsync(line, entry);
                    }
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Schedule(() => OnError?.Invoke(ex));
            }
            finally
            {
                streams.TryRemove(key, out _);
                Schedule(() => OnConnectionChanged?.Invoke(false));
            }
        }

        // ── UDP ────────────────────────────────────────────────────────────────

        private async Task UdpReceiveLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await udpClient.ReceiveAsync();
                    var raw    = Encoding.UTF8.GetString(result.Buffer).TrimEnd('\n', '\r');
                    if (!string.IsNullOrWhiteSpace(raw))
                        DeliverUdp(raw);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { Schedule(() => OnError?.Invoke(ex)); }
            }
        }

        // ── 共用 ───────────────────────────────────────────────────────────────

        private const string StatusPrefix = "EDGELINK_STATUS:";
        private const string PingPrefix   = "EDGELINK_PING:";

        private async Task DeliverAsync(string raw, StreamEntry entry)
        {
            // PING → 立即回 PONG，不進事件佇列
            if (raw.StartsWith(PingPrefix, StringComparison.Ordinal))
            {
                string ticks = raw.Substring(PingPrefix.Length);
                byte[] pong  = Encoding.UTF8.GetBytes($"EDGELINK_PONG:{ticks}\n");
                await WriteAsync(entry, pong);
                return;
            }

            if (raw.StartsWith(StatusPrefix, StringComparison.Ordinal))
            {
                // EDGELINK_STATUS:{CONNECTED|DISCONNECTED}:{portName}@{endpoint}
                string payload   = raw.Substring(StatusPrefix.Length);
                int    statusSep = payload.IndexOf(':');
                if (statusSep >= 0)
                {
                    bool   connected = payload.Substring(0, statusSep) == "CONNECTED";
                    string rest      = payload.Substring(statusSep + 1);
                    int    atIdx     = rest.IndexOf('@');
                    string portName  = atIdx >= 0 ? rest.Substring(0, atIdx) : rest;
                    string endpoint  = atIdx >= 0 ? rest.Substring(atIdx + 1) : "";
                    Schedule(() => OnDeviceStatusChanged?.Invoke(portName, endpoint, connected));
                }
                return;
            }

            var parsed = parser?.Parse(raw);
            var msg    = new IotMessage(raw, parsed);
            Schedule(() => { LatestMessage = msg; OnMessage?.Invoke(msg); });
        }

        private void DeliverUdp(string raw)
        {
            if (raw.StartsWith(StatusPrefix, StringComparison.Ordinal))
            {
                string payload = raw.Substring(StatusPrefix.Length);
                int    sep     = payload.IndexOf(':');
                if (sep >= 0)
                {
                    bool   connected = payload.Substring(0, sep) == "CONNECTED";
                    string rest      = payload.Substring(sep + 1);
                    int    atIdx     = rest.IndexOf('@');
                    string portName  = atIdx >= 0 ? rest.Substring(0, atIdx) : rest;
                    string endpoint  = atIdx >= 0 ? rest.Substring(atIdx + 1) : "";
                    Schedule(() => OnDeviceStatusChanged?.Invoke(portName, endpoint, connected));
                }
                return;
            }

            var parsed = parser?.Parse(raw);
            var msg    = new IotMessage(raw, parsed);
            Schedule(() => { LatestMessage = msg; OnMessage?.Invoke(msg); });
        }

        private async Task WriteAsync(StreamEntry entry, byte[] bytes)
        {
            await entry.WriteLock.WaitAsync();
            try   { await entry.Stream.WriteAsync(bytes, 0, bytes.Length); }
            catch { }
            finally { entry.WriteLock.Release(); }
        }

        private void Schedule(Action action) => pendingEvents.Enqueue(action);
    }
}
