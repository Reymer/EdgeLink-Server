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
                UnityEngine.Debug.LogError($"[UdpData] 釋放 CancellationTokenSource 時發生非預期錯誤: {ex.Message}");
            }

            CancellationTokenSource = null;
        }

        if (udpClient != null)
        {
            try
            {
                udpClient.Close();
            }
            catch (System.Exception)
            {
                // 關閉失敗通常可忽略
            }

            try
            {
                udpClient.Dispose();
            }
            catch (System.Exception)
            {
                // 釋放失敗通常可忽略
            }

            udpClient = null;
        }
    }
}
