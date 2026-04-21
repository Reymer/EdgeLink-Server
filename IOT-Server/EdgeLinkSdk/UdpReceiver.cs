using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EdgeLink
{
    /// <summary>
    /// 監聽 UDP 封包，接收 IoT Server UDP 連接器轉發的資料。
    /// 在 MonoBehaviour.Update() 裡呼叫 Flush()，事件才會在 Unity 主執行緒觸發。
    /// </summary>
    public class UdpReceiver : IDisposable
    {
        // ── 事件 ───────────────────────────────────────────────────────────────

        /// <summary>每筆新訊息到達時觸發。</summary>
        public event Action<IotMessage> OnMessage;

        /// <summary>發生錯誤時觸發。</summary>
        public event Action<Exception> OnError;

        // ── 公開資料 ───────────────────────────────────────────────────────────

        /// <summary>最後一筆收到的訊息，可直接在 Update() 讀取，不需要靠事件。</summary>
        public IotMessage LatestMessage { get; private set; }

        /// <summary>目前是否正在監聽。</summary>
        public bool IsListening { get; private set; }

        // ── 內部欄位 ───────────────────────────────────────────────────────────

        private readonly MaskParser parser;
        private UdpClient udpClient;
        private CancellationTokenSource cancelSource;
        private readonly ConcurrentQueue<Action> pendingEvents = new ConcurrentQueue<Action>();

        /// <param name="definition">
        /// 韌體工程師提供的遮罩定義（選填）。
        /// 傳入後每筆封包會自動解析成欄位，否則只有 Raw 原始字串。
        /// </param>
        public UdpReceiver(MaskDefinition definition = null)
        {
            parser = definition != null ? new MaskParser(definition) : null;
        }

        /// <summary>
        /// 開始監聽指定 port 的 UDP 封包。
        /// IoT Server UDP 的目標 IP 和 Port 要對應到這台機器的這個 port。
        /// </summary>
        public void Start(int port)
        {
            if (cancelSource != null)
                throw new InvalidOperationException("UdpReceiver 已啟動，請先呼叫 Stop()。");

            cancelSource = new CancellationTokenSource();
            udpClient    = new UdpClient(port);
            IsListening  = true;

            Task.Run(() => ReceiveLoop(cancelSource.Token));
        }

        /// <summary>停止監聽。</summary>
        public void Stop()
        {
            cancelSource?.Cancel();
            udpClient?.Close();
            cancelSource = null;
            udpClient    = null;
            IsListening  = false;
        }

        /// <summary>
        /// 在 MonoBehaviour.Update() 裡呼叫此方法，讓事件在主執行緒觸發。
        /// </summary>
        public void Flush()
        {
            while (pendingEvents.TryDequeue(out var action))
            {
                try { action(); }
                catch { }
            }
        }

        public void Dispose() => Stop();

        // ── 內部實作 ───────────────────────────────────────────────────────────

        private async Task ReceiveLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await udpClient.ReceiveAsync();
                    var raw    = Encoding.UTF8.GetString(result.Buffer).TrimEnd('\n', '\r');

                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    var parsed = parser?.Parse(raw);
                    var msg    = new IotMessage(raw, parsed);

                    pendingEvents.Enqueue(() =>
                    {
                        LatestMessage = msg;
                        OnMessage?.Invoke(msg);
                    });
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    pendingEvents.Enqueue(() => OnError?.Invoke(ex));
                }
            }
        }
    }
}
