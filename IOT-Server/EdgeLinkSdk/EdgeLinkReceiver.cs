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
            udpClient?.Close();
            cancelSource = null;
            tcpListener  = null;
            udpClient    = null;
            IsListening  = false;
        }

        /// <summary>在 MonoBehaviour.Update() 呼叫，讓事件在主執行緒觸發。</summary>
        public void Flush()
        {
            while (pendingEvents.TryDequeue(out var action))
            {
                try { action(); } catch { }
            }
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
            Schedule(() => OnConnectionChanged?.Invoke(true));
            try
            {
                using (client)
                using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8))
                {
                    string line;
                    while (!token.IsCancellationRequested &&
                           (line = await reader.ReadLineAsync()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            Deliver(line);
                    }
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Schedule(() => OnError?.Invoke(ex));
            }
            finally
            {
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
                        Deliver(raw);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { Schedule(() => OnError?.Invoke(ex)); }
            }
        }

        // ── 共用 ───────────────────────────────────────────────────────────────

        private void Deliver(string raw)
        {
            var parsed = parser?.Parse(raw);
            var msg    = new IotMessage(raw, parsed);
            Schedule(() => { LatestMessage = msg; OnMessage?.Invoke(msg); });
        }

        private void Schedule(Action action) => pendingEvents.Enqueue(action);
    }
}
