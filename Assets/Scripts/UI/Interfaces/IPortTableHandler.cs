public interface IPortTableHandler
{
    void OnRemove(PortData portData);
    void OnConnect(PortData portData);
    void OnDisconnectedPort(PortData portData);
    void OnMaskType(PortData portData);
    void OnMonitorConsole(PortData portData);
}
