using System;
using UnityEngine;
using VARLive.ApexNetwork;

public static class NetworkService
{
    private class NetworkCallbackProxy : IApexNetworkCallback
    {
        private const string ServiceApiChannelName = "ServiceApi";

        public void Init()
        {
            RegisterNetworkCallback(this);
        }

        public void OnConnect()
        {
            NetworkSystem.CreateChannel(ServiceApiChannelName);
        }

        public void OnDisconnect()
        {
            ServiceApi.UnbindNetworkEvent();

            UnregisterNetworkCallback(this);
        }

        public void OnRemoveMember(User user)
        {

        }

        public void OnAddMember(User user)
        {
        }

        public void OnCreateChannel(string channelName, Channel channel)
        {
            Channel serviceChannel = NetworkSystem.GetChannel(ServiceApiChannelName);
            if (serviceChannel != null)
            {
                ServiceApi.SetChannel(serviceChannel);
                ServiceApi.BindNetworkEvent();
                ServiceApi.SendOnConnectCallback();
            }
        }

        public void SetupNetworkEvent(Channel channel)
        {

        }
    }

    /// <summary>
    /// 網路系統
    /// </summary>
    public static ApexNetworkSystem NetworkSystem { get; private set; } = new ApexNetworkSystem();

    /// <summary>
    /// 前端軟體串接的API
    /// </summary>
    public static ServiceApi ServiceApi { get; private set; } = new ServiceApi();

    /// <summary>
    /// 是否已經連線
    /// </summary>
    public static bool IsConnected => NetworkSystem.IsConnected;

    private static readonly NetworkCallbackProxy proxy = new();

    /// <summary>
    /// 註冊網路功能Callback介面
    /// </summary>
    public static void RegisterNetworkCallback(IApexNetworkCallback callback)
    {
        NetworkSystem.RegisterNetworkCallback(callback);
    }

    /// <summary>
    /// 解註冊網路功能Callback介面
    /// </summary>
    public static void UnregisterNetworkCallback(IApexNetworkCallback callback)
    {
        NetworkSystem.UnregisterNetworkCallback(callback);
    }

    /// <summary>
    /// 註冊用於Client端的服務功能Callback介面
    /// </summary>
    public static void RegisterServiceCallbackForClient(IServiceCallbackForClient callback)
    {
        ServiceApi.RegisterServiceCallbackForClient(callback);
    }

    /// <summary>
    /// 解註冊用於Client端的服務功能Callback介面
    /// </summary>
    public static void UnregisterServiceCallbackForClient(IServiceCallbackForClient callback)
    {
        ServiceApi.UnregisterServiceCallbackForClient(callback);
    }

    /// <summary>
    /// 註冊用於Server端的服務功能Callback介面
    /// </summary>
    public static void RegisterServiceCallbackForServer(IServiceCallbackForServer callback)
    {
        ServiceApi.RegisterServiceCallbackForServer(callback);
    }

    /// <summary>
    /// 解註冊用於Server端的服務功能Callback介面
    /// </summary>
    public static void UnregisterServiceCallbackForServer(IServiceCallbackForServer callback)
    {
        ServiceApi.UnregisterServiceCallbackForServer(callback);
    }

    /// <summary>
    /// 使用127.0.0.1連線
    /// </summary>
    public static void Connect()
    {
        Connect("127.0.0.1", string.Empty);
    }

    /// <summary>
    /// 連線至指定的IP
    /// </summary>
    /// <param name="ipAddress">ApexCore電腦的IP</param>
    /// <param name="appKey">用來區分不同遊戲的Key</param>
    public static void Connect(string ipAddress, string appKey)
    {
        proxy.Init();
        NetworkSystem.Init(ipAddress, appKey);
        NetworkSystem.Connect();
    }

    /// <summary>
    /// 連線至指定的IP和頻道
    /// </summary>
    /// <param name="ipAddress">ApexCore電腦的IP</param>
    /// <param name="appKey">用來區分不同遊戲的Key</param>
    /// <param name="channelName">指定的網路頻道</param>
    public static void Connect(string ipAddress, string appKey, string channelName)
    {
        proxy.Init();
        NetworkSystem.Init(ipAddress, appKey);
        NetworkSystem.Connect(channelName);
    }

    /// <summary>
    /// 斷線
    /// </summary>
    public static void Disconnect()
    {
        NetworkSystem.Disconnect();
    }
}
