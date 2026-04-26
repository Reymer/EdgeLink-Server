using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

public class PollSlot
{
    public PendingRequest Requester;
    public byte[] Data;
}

public class TCPClientData : DisposableBase
{
    public TcpClient tcpClient;
    public CancellationTokenSource CancellationTokenSource = new();
    public Task HeartbeatTask;
    public PortData portData;

    // --- 反向路由：Serial 模式 ---
    /// <summary>排隊等送出的請求（serial 模式），上限 64 筆，超過直接丟棄</summary>
    public readonly AsyncMessageQueue<(PendingRequest requester, byte[] data)> RequestQueue = new(64);
    /// <summary>目前正在等設備回應的請求（serial / polling 模式）</summary>
    public volatile PendingRequest CurrentPendingRequester;
    /// <summary>設備回應到達時完成此 signal（serial / polling 模式）</summary>
    public volatile TaskCompletionSource<bool> ResponseSignal;

    // --- 反向路由：Polling 模式 ---
    /// <summary>最新一筆待送請求（polling 模式），新請求直接覆蓋舊的</summary>
    public volatile PollSlot LatestPollRequest;
    /// <summary>有新請求時觸發 processor（polling 模式）</summary>
    public readonly SemaphoreSlim PollTrigger = new(0, 1);

    // --- 反向路由：Concurrent 模式 ---
    /// <summary>correlationId → PendingRequest（concurrent 模式）</summary>
    public readonly ConcurrentDictionary<string, PendingRequest> PendingRequests = new();

    // --- 寫入保護 ---
    /// <summary>
    /// 保護裝置端 NetworkStream 的寫入鎖。
    /// NotifyAsync（STATUS）與 ProcessSerialQueueAsync / TrySendToClient（資料）
    /// 可能從不同 ThreadPool 執行緒並發寫入同一條 stream，此鎖確保任何時刻只有一個寫入。
    /// </summary>
    public readonly SemaphoreSlim DeviceWriteLock = new(1, 1);

    protected override void DisposeManagedResources()
    {
        if (CancellationTokenSource != null)
        {
            if (!CancellationTokenSource.IsCancellationRequested)
                CancellationTokenSource.Cancel();

            try
            {
                CancellationTokenSource.Dispose();
            }
            catch (System.ObjectDisposedException)
            {
                // 已經被釋放，靜默忽略
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[TCPClientData] 釋放 CancellationTokenSource 時發生非預期錯誤: {ex.Message}");
            }
            CancellationTokenSource = null;
        }

        if (HeartbeatTask != null && !HeartbeatTask.IsCompleted)
        {
            try
            {
                HeartbeatTask.Wait(2000);
            }
            catch (System.AggregateException) { }
            catch (System.Exception) { }

            HeartbeatTask = null;
        }

        // 清除等待中的 signal，避免 queue processor 永遠等待
        ResponseSignal?.TrySetCanceled();
        ResponseSignal = null;
        CurrentPendingRequester = null;
        LatestPollRequest = null;
        try { PollTrigger.Release(); } catch { }
        try { DeviceWriteLock.Dispose(); } catch { }

        foreach (var kv in PendingRequests)
            PendingRequests.TryRemove(kv.Key, out _);

        if (tcpClient != null)
        {
            try { tcpClient.Close(); } catch (System.Exception) { }
            try { tcpClient.Dispose(); } catch (System.Exception) { }
            tcpClient = null;
        }
    }
}
