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

    // 使用私有欄位以確保原子操作
    private int totalConnections = 0;
    private int currentConnections = 0;
    private long totalReceivedBytes = 0;
    private long totalSentBytes = 0;

    public int TotalConnections => totalConnections;
    public int CurrentConnections => currentConnections;
    public long TotalReceivedBytes => totalReceivedBytes;
    public long TotalSentBytes => totalSentBytes;

    public string sourceData = string.Empty;

    private int recvCount = 0;
    private int sendCount = 0;
    public int RecvCount => recvCount;
    public int SendCount => sendCount;

    // 連接計數的原子操作
    public void IncrementTotalConnections()
    {
        Interlocked.Increment(ref totalConnections);
    }

    public void IncrementCurrentConnections()
    {
        Interlocked.Increment(ref currentConnections);
    }

    public void DecrementCurrentConnections()
    {
        // ✅ P1.2 修復：使用原子操作避免競態條件
        int newValue;
        int currentValue;
        do
        {
            currentValue = currentConnections;
            newValue = currentValue > 0 ? currentValue - 1 : 0;
        } while (Interlocked.CompareExchange(ref currentConnections, newValue, currentValue) != currentValue);
    }

    // 字節計數的原子操作
    public void AddReceivedBytes(long bytes)
    {
        Interlocked.Add(ref totalReceivedBytes, bytes);
    }

    public void AddSentBytes(long bytes)
    {
        Interlocked.Add(ref totalSentBytes, bytes);
    }

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