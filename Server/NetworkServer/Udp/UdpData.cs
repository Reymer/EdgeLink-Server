using System.Net.Sockets;
using EdgeLink.Infrastructure;
using EdgeLink.NetworkServer.Base;
using EdgeLink.NetworkServer.Base.Models;

namespace EdgeLink.NetworkServer.Udp;

public class UdpData : DisposableBase
{
    public UdpClient? udpClient;
    public CancellationTokenSource CancellationTokenSource = new();
    public PortData portData = null!;
    public string SourceData = string.Empty;

    protected override void DisposeManagedResources()
    {
        if (CancellationTokenSource != null)
        {
            if (!CancellationTokenSource.IsCancellationRequested)
                CancellationTokenSource.Cancel();
            try { CancellationTokenSource.Dispose(); }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { AppLogger.Error($"[UdpData] Unexpected error disposing CTS: {ex.Message}"); }
            CancellationTokenSource = null!;
        }

        if (udpClient != null)
        {
            try { udpClient.Close(); } catch { }
            try { udpClient.Dispose(); } catch { }
            udpClient = null;
        }
    }
}
