using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EdgeLink
{
    /// <summary>
    /// TCP client that connects to an EdgeLink Server TCP Server Port.
    /// Automatically handles EDGELINK_PING/PONG keepalive.
    /// Thread-safe: messages are queued and can be dequeued from the Unity main thread.
    /// </summary>
    public class EdgeLinkClient : IDisposable
    {
        /// <summary>Fires on the background thread when a business message arrives.</summary>
        public event Action<string>?    OnMessage;
        public event Action?            OnConnected;
        public event Action?            OnDisconnected;
        public event Action<Exception>? OnError;

        public bool IsConnected => _tcpClient?.Connected == true && !_disposed;
        public string Host { get; }
        public int    Port { get; }

        private TcpClient?              _tcpClient;
        private NetworkStream?          _stream;
        private CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<string> _queue = new();
        private bool _disposed;
        private bool _autoReconnect   = true;
        private int  _reconnectDelayMs = 5000;

        public EdgeLinkClient(string host, int port)
        {
            Host = host;
            Port = port;
        }

        /// <param name="enable">Auto-reconnect on disconnect.</param>
        /// <param name="delayMs">Milliseconds between reconnect attempts.</param>
        public void SetAutoReconnect(bool enable, int delayMs = 5000)
        {
            _autoReconnect    = enable;
            _reconnectDelayMs = delayMs;
        }

        /// <summary>Connect and start background read loop.</summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EdgeLinkClient));
            _cts.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await ConnectCoreAsync(_cts.Token);
            _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
        }

        private async Task ConnectCoreAsync(CancellationToken ct)
        {
            _tcpClient?.Dispose();
            _tcpClient = new TcpClient { NoDelay = true };
            await _tcpClient.ConnectAsync(Host, Port);
            _stream = _tcpClient.GetStream();
            OnConnected?.Invoke();
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            var buf    = new byte[4096];
            var lineBuf = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_stream == null || !IsConnected)
                    {
                        OnDisconnected?.Invoke();
                        if (!_autoReconnect) return;
                        await Task.Delay(_reconnectDelayMs, ct);
                        await ConnectCoreAsync(ct);
                        lineBuf.Clear();
                        continue;
                    }

                    int read = await _stream!.ReadAsync(buf, 0, buf.Length, ct);
                    if (read == 0)
                    {
                        _tcpClient?.Dispose();
                        _tcpClient = null;
                        continue;
                    }

                    string chunk = Encoding.UTF8.GetString(buf, 0, read);
                    lineBuf.Append(chunk);

                    int idx;
                    while ((idx = FindNewline(lineBuf)) >= 0)
                    {
                        string line = lineBuf.ToString(0, idx).Trim();
                        lineBuf.Remove(0, idx + 1);
                        if (line.Length > 0) HandleLine(line);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex);
                    _tcpClient?.Dispose();
                    _tcpClient = null;
                    if (!_autoReconnect) return;
                    OnDisconnected?.Invoke();
                    try { await Task.Delay(_reconnectDelayMs, ct); } catch { return; }
                    try { await ConnectCoreAsync(ct); lineBuf.Clear(); } catch { }
                }
            }
        }

        private static int FindNewline(StringBuilder sb)
        {
            for (int i = 0; i < sb.Length; i++)
                if (sb[i] == '\n') return i;
            return -1;
        }

        private void HandleLine(string line)
        {
            if (line.StartsWith("EDGELINK_PING:", StringComparison.Ordinal))
            {
                string hex = line.Substring(14);
                _ = SendRawAsync($"EDGELINK_PONG:{hex}\n");
                return;
            }
            if (line.StartsWith("EDGELINK_", StringComparison.Ordinal)) return;

            _queue.Enqueue(line);
            OnMessage?.Invoke(line);
        }

        /// <summary>Send a message to EdgeLink (newline appended if missing).</summary>
        public async Task SendAsync(string message)
        {
            if (_stream == null || !IsConnected)
                throw new InvalidOperationException("Not connected to EdgeLink.");

            if (!message.EndsWith("\n")) message += "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            await _stream.WriteAsync(bytes, 0, bytes.Length);
        }

        private async Task SendRawAsync(string raw)
        {
            try
            {
                if (_stream == null) return;
                byte[] b = Encoding.UTF8.GetBytes(raw);
                await _stream.WriteAsync(b, 0, b.Length);
            }
            catch { }
        }

        /// <summary>
        /// Dequeue one received message. Call this in Unity Update() to process
        /// messages on the main thread without locking.
        /// </summary>
        public bool TryDequeue(out string message) => _queue.TryDequeue(out message!);

        public void Disconnect()
        {
            _cts.Cancel();
            _tcpClient?.Dispose();
            _tcpClient = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
            _cts.Dispose();
        }
    }
}
