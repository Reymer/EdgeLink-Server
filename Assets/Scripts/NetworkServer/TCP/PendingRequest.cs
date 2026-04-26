using System;
using System.Net.Sockets;

/// <summary>
/// 代表一筆等待設備回應的請求，記錄是哪個 Frontend Client 發的
/// </summary>
public class PendingRequest
{
    public string ClientKey;
    public NetworkStream Stream;
    public DateTime EnqueueTime;
}
