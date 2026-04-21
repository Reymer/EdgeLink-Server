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
        // ✅ 修復：先取消 CancellationTokenSource，心跳任務會自動停止
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
                // 只記錄非預期的異常
                UnityEngine.Debug.LogError($"[TCPClientData] 釋放 CancellationTokenSource 時發生非預期錯誤: {ex.Message}");
            }
            CancellationTokenSource = null;
        }

        // 等待心跳任務回應取消（最多 2000ms，給 token 取消後的清理足夠時間）
        if (HeartbeatTask != null && !HeartbeatTask.IsCompleted)
        {
            try
            {
                HeartbeatTask.Wait(2000);
            }
            catch (System.AggregateException)
            {
                // 任務可能因為 OperationCanceledException 而結束，這是正常的
            }
            catch (System.Exception)
            {
                // 靜默忽略其他異常（資源清理階段）
            }

            HeartbeatTask = null;
        }

        if (tcpClient != null)
        {
            try
            {
                tcpClient.Close();
            }
            catch (System.Exception)
            {
                // 關閉失敗通常可忽略
            }

            try
            {
                tcpClient.Dispose();
            }
            catch (System.Exception)
            {
                // 釋放失敗通常可忽略
            }

            tcpClient = null;
        }
    }
}