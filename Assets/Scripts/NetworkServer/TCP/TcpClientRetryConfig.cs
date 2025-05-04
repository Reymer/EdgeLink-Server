using System;

[Serializable]
public class TcpClientRetryConfig
{
    /// <summary>
    /// 首次連線重試次數
    /// </summary>
    public int MaxRetryFirst = 3;
    /// <summary>
    /// 首次連線重試次數
    /// </summary>
    public int MaxRetrySubsequent = 10;
    /// <summary>
    /// 首次連線延遲 (ms)
    /// </summary>
    public int InitialDelayMs = 2000;
    /// <summary>
    /// 指數回退最大延遲 (ms)
    /// </summary>
    public int MaxDelayMs = 30000;
    /// <summary>
    /// 心跳間隔 (ms)
    /// </summary>
    public int HeartbeatIntervalMs = 5000;
}
