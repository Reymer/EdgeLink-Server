using static NetworkPortManager;
using System.Net.Sockets;
using System.Net;
using System.Threading;

public class TCPServerData : DisposableBase
{
    public TcpListener tcpListener;
    public CancellationTokenSource CancellationTokenSource = new();
    public AsyncMessageQueue<byte[]> asyncMessageQueue = new();
    public IPEndPoint RemoteEndPoint;
    public PortData portData;
    public int TotalConnections { get; set; } = 0;    
    public int CurrentConnections { get; set; } = 0;  
    public long TotalReceivedBytes { get; set; } = 0;  
    public long TotalSentBytes { get; set; } = 0;     

    public string sourceData = string.Empty;

    private int recvCount = 0;
    private int sendCount = 0;
    public int RecvCount => recvCount;
    public int SendCount => sendCount;

    public void IncrementRecvCount()
    {
        Interlocked.Increment(ref recvCount);
    }
    public void IncrementSendCount()
    {
        Interlocked.Increment(ref sendCount);
    }

    protected override void DisposeManagedResources()
    {
        if (CancellationTokenSource != null)
        {
            if (!CancellationTokenSource.IsCancellationRequested)
                CancellationTokenSource.Cancel();

            CancellationTokenSource.Dispose();
            CancellationTokenSource = null;
        }

        tcpListener?.Stop();
        tcpListener = null;
    }
}