using System.Net.Sockets;
using System.Threading;
using static NetworkPortManager;

public class UdpData : DisposableBase
{
    public UdpClient udpClient;
    public CancellationTokenSource CancellationTokenSource = new();
    public PortData portData;
    public string SourceData = string.Empty;

    /// <summary>
    /// 釋放資源
    /// </summary>
    protected override void DisposeManagedResources()
    {
        if (CancellationTokenSource != null)
        {
            if (!CancellationTokenSource.IsCancellationRequested)
            {
                CancellationTokenSource.Cancel();
            }

            CancellationTokenSource.Dispose();
            CancellationTokenSource = null;
        }

        if (udpClient != null)
        {
            try { udpClient.Close(); } catch { }
            try { udpClient.Dispose(); } catch { }
            udpClient = null;
        }
    }
}
