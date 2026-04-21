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
    /// <summary>
    /// 監聽指定 port，接收 IoT Server TCP Client 推送過來的訊息。
    /// 在 MonoBehaviour.Update() 裡呼叫 Flush()，事件才會在 Unity 主執行緒觸發。
    /// </summary>
    public class IotReceiver : IDisposable
    {
        public event Action<IotMessage> OnMessage;
        public event Action<bool> OnConnectionChanged;
        public event Action<Exception> OnError;

        public IotMessage LatestMessage { get; private set; }
        public bool IsListening { get; private set; }

        private readonly MaskParser parser;
        private TcpListener listener;
        private CancellationTokenSource cancelSource;
        private readonly ConcurrentQueue<Action> pendingEvents = new ConcurrentQueue<Action>();

        public IotReceiver(MaskDefinition definition = null)
        {
            parser = definition != null ? new MaskParser(definition) : null;
        }

        public void Start(int port)
        {
            if (cancelSource != null)
                throw new InvalidOperationException("IotReceiver 已啟動，請先呼叫 Stop()。");

            cancelSource = new CancellationTokenSource();
            listener     = new TcpListener(IPAddress.Any, port);
            listener.Start();
            IsListening  = true;

            Task.Run(() => WaitForConnections(cancelSource.Token));
        }

        public void Stop()
        {
            cancelSource?.Cancel();
            listener?.Stop();
            cancelSource = null;
            listener     = null;
            IsListening  = false;
        }

        public void Flush()
        {
            while (pendingEvents.TryDequeue(out var action))
            {
                try { action(); } catch { }
            }
        }

        public void Dispose() => Stop();

        private async Task WaitForConnections(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    _ = Task.Run(() => ReceiveData(client, token));
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { Schedule(() => OnError?.Invoke(ex)); }
            }
        }

        private async Task ReceiveData(TcpClient client, CancellationToken token)
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
                            HandleMessage(line);
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

        private void HandleMessage(string raw)
        {
            var parsed = parser?.Parse(raw);
            var msg    = new IotMessage(raw, parsed);
            Schedule(() => { LatestMessage = msg; OnMessage?.Invoke(msg); });
        }

        private void Schedule(Action action) => pendingEvents.Enqueue(action);
    }
}
