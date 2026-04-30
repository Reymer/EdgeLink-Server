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
    /// TCP listener for when EdgeLink Server is configured as TCP Client mode,
    /// pushing data to Unity. Unity listens on a local port; EdgeLink connects in.
    /// </summary>
    public class EdgeLinkTcpListener : IDisposable
    {
        public event Action<string>?    OnMessage;
        public event Action?            OnConnected;
        public event Action?            OnDisconnected;
        public event Action<Exception>? OnError;

        public int  LocalPort  { get; }
        public bool IsRunning  { get; private set; }

        private TcpListener?            _listener;
        private CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<string> _queue = new();
        private bool _disposed;

        public EdgeLinkTcpListener(int localPort)
        {
            LocalPort = localPort;
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EdgeLinkTcpListener));
            if (IsRunning) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, LocalPort);
            _listener.Start();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener!.AcceptTcpClientAsync();
                    _ = Task.Run(() => ReadLoopAsync(client, ct), ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { OnError?.Invoke(ex); }
            }
        }

        private async Task ReadLoopAsync(TcpClient client, CancellationToken ct)
        {
            OnConnected?.Invoke();
            var stream  = client.GetStream();
            var buf     = new byte[4096];
            var lineBuf = new StringBuilder();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int read = await stream.ReadAsync(buf, 0, buf.Length, ct);
                    if (read == 0) break;

                    lineBuf.Append(Encoding.UTF8.GetString(buf, 0, read));

                    int idx;
                    while ((idx = FindNewline(lineBuf)) >= 0)
                    {
                        string line = lineBuf.ToString(0, idx).Trim();
                        lineBuf.Remove(0, idx + 1);
                        if (line.Length > 0) await HandleLineAsync(stream, line);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { OnError?.Invoke(ex); }
            finally
            {
                client.Dispose();
                OnDisconnected?.Invoke();
            }
        }

        private async Task HandleLineAsync(NetworkStream stream, string line)
        {
            if (line.StartsWith("EDGELINK_PING:", StringComparison.Ordinal))
            {
                string hex = line.Substring(14);
                try
                {
                    byte[] pong = Encoding.UTF8.GetBytes($"EDGELINK_PONG:{hex}\n");
                    await stream.WriteAsync(pong, 0, pong.Length);
                }
                catch { }
                return;
            }
            if (line.StartsWith("EDGELINK_", StringComparison.Ordinal)) return;

            _queue.Enqueue(line);
            OnMessage?.Invoke(line);
        }

        private static int FindNewline(StringBuilder sb)
        {
            for (int i = 0; i < sb.Length; i++)
                if (sb[i] == '\n') return i;
            return -1;
        }

        public bool TryDequeue(out string message) => _queue.TryDequeue(out message!);

        public void Stop()
        {
            _cts.Cancel();
            _listener?.Stop();
            IsRunning = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts.Dispose();
        }
    }
}
