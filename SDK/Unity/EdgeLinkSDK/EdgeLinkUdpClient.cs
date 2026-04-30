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
    /// UDP 接收端，監聽指定本機 Port，將收到的封包排入佇列。
    /// UDP 無連線狀態，EdgeLink Server 不會發送 PING/PONG。
    /// </summary>
    public class EdgeLinkUdpClient : IDisposable
    {
        public event Action<string>?    OnMessage;
        public event Action<Exception>? OnError;

        public int LocalPort { get; }
        public bool IsRunning => !_disposed && _cts != null && !_cts.IsCancellationRequested;

        private UdpClient?              _udp;
        private CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<string> _queue = new();
        private bool _disposed;

        public EdgeLinkUdpClient(int localPort)
        {
            LocalPort = localPort;
        }

        /// <summary>開始監聽，背景執行緒接收封包。</summary>
        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EdgeLinkUdpClient));
            _cts = new CancellationTokenSource();
            _udp = new UdpClient(LocalPort);
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udp!.ReceiveAsync();
                    string msg = Encoding.UTF8.GetString(result.Buffer).Trim();
                    if (string.IsNullOrEmpty(msg)) continue;

                    _queue.Enqueue(msg);
                    OnMessage?.Invoke(msg);
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException)    { return; }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex);
                }
            }
        }

        /// <summary>從主執行緒 Update() 呼叫，取出一筆收到的訊息。</summary>
        public bool TryDequeue(out string message) => _queue.TryDequeue(out message!);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _udp?.Close();
            _udp?.Dispose();
            _cts.Dispose();
        }
    }
}
