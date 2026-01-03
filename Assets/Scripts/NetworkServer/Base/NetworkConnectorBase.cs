using System.Threading.Tasks;

/// <summary>
/// 網路連接器抽象基類
/// 定義所有協議連接器的通用介面
/// </summary>
public abstract class NetworkConnectorBase
{
    /// <summary>
    /// 添加端口
    /// </summary>
    /// <param name="portData">端口數據</param>
    public abstract void AddPort(PortData portData);

    /// <summary>
    /// 移除端口
    /// </summary>
    /// <param name="portData">端口數據</param>
    /// <returns></returns>
    public abstract Task RemovePort(PortData portData);

    /// <summary>
    /// 連接端口
    /// </summary>
    /// <param name="portData">端口數據</param>
    public abstract void Connect(PortData portData);

    /// <summary>
    /// 斷開連接
    /// </summary>
    /// <param name="portData">端口數據</param>
    /// <returns></returns>
    public abstract Task Disconnect(PortData portData);

    /// <summary>
    /// 重啟端口
    /// </summary>
    /// <param name="portData">端口數據</param>
    /// <returns></returns>
    public abstract Task RestartPort(PortData portData);

    /// <summary>
    /// 關閉所有連接
    /// </summary>
    /// <returns></returns>
    public abstract Task ShutdownAsync();
}
