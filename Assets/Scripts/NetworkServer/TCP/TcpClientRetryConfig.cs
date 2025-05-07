public class TcpClientRetryConfig
{
    /// <summary>首次連線最大重試次數，設為 -1 表示無限</summary>
    public int MaxRetryFirst { get; set; } = -1;

    /// <summary>重連最大重試次數，設為 -1 表示無限</summary>
    public int MaxRetrySubsequent { get; set; } = -1;

    public int InitialDelayMs { get; set; } = 1000;
    public int HeartbeatIntervalMs { get; set; } = 5000;
}
