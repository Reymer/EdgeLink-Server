using static NetworkPortManager;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TCPClientData : DisposableBase
{
    public TcpClient tcpClient;
    public CancellationTokenSource CancellationTokenSource = new();
    public Task HeartbeatTask;

    public PortData portData;

    protected override void DisposeManagedResources()
    {
        if (CancellationTokenSource != null)
        {
            if (!CancellationTokenSource.IsCancellationRequested)
                CancellationTokenSource.Cancel();

            try { CancellationTokenSource.Dispose(); } catch { }
            CancellationTokenSource = null;
        }

        if (tcpClient != null)
        {
            try { tcpClient.Close(); } catch { }
            try { tcpClient.Dispose(); } catch { }
            tcpClient = null;
        }
    }
}