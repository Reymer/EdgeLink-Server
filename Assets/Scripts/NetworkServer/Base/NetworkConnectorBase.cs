using Cysharp.Threading.Tasks;

/// <summary>
/// 網路連接器抽象基類
/// 定義所有協議連接器的通用介面
/// </summary>
public abstract class NetworkConnectorBase
{
    public abstract void AddPort(PortData portData);
    public abstract UniTask RemovePort(PortData portData);
    public abstract void Connect(PortData portData);
    public abstract UniTask Disconnect(PortData portData);
    public abstract UniTask RestartPort(PortData portData);
    public abstract UniTask ShutdownAsync();
}
