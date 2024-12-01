using System;
using System.Collections.Generic;
using VARLive.ApexNetwork;

public struct UserData
{
    public int userID;
    public string computerName;
}

public interface IServiceCallbackForServer
{
    /// <summary>
    /// [All]當連上服務功能時
    /// </summary>
    void OnConnect();
    /// <summary>
    /// [Server]當有玩家註冊時的Callback
    /// </summary>
    void OnRegisterUser(UserData userData);
    /// <summary>
    /// [Server]當有玩家解註冊時的Callback
    /// </summary>
    void OnUnregisterUser(int userID);
    /// <summary>
    /// [Server]當有玩家準備好時的Callback
    /// </summary>
    void OnReady(int userID);
    /// <summary>
    /// [Server]當有玩家要關閉遊戲時的Callback
    /// </summary>
    void OnQuit(int userID);
    /// <summary>
    /// [Server]當收到已開始遊戲時的Callback
    /// </summary>
    void OnReceiveStart(int userID);
    /// <summary>
    /// [Server]當收到已停止遊戲時的Callback
    /// </summary>
    void OnReceiveStop(int userID);
}

public interface IServiceCallbackForClient
{
    /// <summary>
    /// [All]當連上服務功能時
    /// </summary>
    void OnConnect();
    /// <summary>
    /// [Client]當收到設定檔時
    /// </summary>
    void OnReciveConfig(int userID, string json);
    /// <summary>
    /// [Client]當收到開始遊戲請求時的Callback
    /// </summary>
    void OnRequestStart();
    /// <summary>
    /// [Client]當收到停止遊戲請求時的Callback
    /// </summary>
    void OnRequestStop();
}

public class ServiceApi
{
    private class ApiEvent
    {
        public const string RegisterUserEvent = nameof(RegisterUserEvent);
        public const string UnregisterUserEvent = nameof(UnregisterUserEvent);
        public const string ReciveConfig = nameof(ReciveConfig);
        public const string ReadyEvent = nameof(ReadyEvent);
        public const string QuitEvent = nameof(QuitEvent);
        public const string RequestStartEvent = nameof(RequestStartEvent);
        public const string RequestStopEvent = nameof(RequestStopEvent);
        public const string ReceiveStartEvent = nameof(ReceiveStartEvent);
        public const string ReceiveStopEvent = nameof(ReceiveStopEvent);
    }

    private Channel channel;

    #region Callbacks
    private readonly List<IServiceCallbackForClient> callbacksForClient = new();
    private readonly List<IServiceCallbackForServer> callbacksForServer = new();

    public void RegisterServiceCallbackForClient(IServiceCallbackForClient callback)
    {
        if (callbacksForClient.Contains(callback) == false)
        {
            callbacksForClient.Add(callback);
        }
    }

    public void UnregisterServiceCallbackForClient(IServiceCallbackForClient callback)
    {
        if (callbacksForClient.Contains(callback))
        {
            callbacksForClient.Remove(callback);
        }
    }

    public void RegisterServiceCallbackForServer(IServiceCallbackForServer callback)
    {
        if (callbacksForServer.Contains(callback) == false)
        {
            callbacksForServer.Add(callback);
        }
    }

    public void UnregisterServiceCallbackForServer(IServiceCallbackForServer callback)
    {
        if (callbacksForServer.Contains(callback))
        {
            callbacksForServer.Remove(callback);
        }
    }

    public void Callback_OnConnect()
    {
        foreach (var callback in callbacksForServer)
        {
            callback?.OnConnect();
        }
        foreach (var callback in callbacksForClient)
        {
            callback?.OnConnect();
        }
    }

    public void Callback_RegisterUser(UserData userData)
    {
        foreach (var callback in callbacksForServer)
        {
            callback?.OnRegisterUser(userData);
        }
    }

    public void Callback_UnregisterUser(int userID)
    {
        foreach (var callback in callbacksForServer)
        {
            callback?.OnUnregisterUser(userID);
        }
    }

    public void Callback_Ready(int userID)
    {
        foreach (var callback in callbacksForServer)
        {
            callback?.OnReady(userID);
        }
    }

    public void Callback_Quit(int userID)
    {
        foreach (var callback in callbacksForServer)
        {
            callback?.OnQuit(userID);
        }
    }

    public void Callback_ReciveConfig(int userID, string json)
    {
        foreach (var callback in callbacksForClient)
        {
            callback?.OnReciveConfig(userID, json);
        }
    }

    public void Callback_RequestStart()
    {
        foreach (var callback in callbacksForClient)
        {
            callback?.OnRequestStart();
        }
    }

    public void Callback_RequestStop()
    {
        foreach (var callback in callbacksForClient)
        {
            callback?.OnRequestStop();
        }
    }

    public void Callback_ReceiveStart(int userID)
    {
        foreach (var callback in callbacksForServer)
        {
            callback?.OnReceiveStart(userID);
        }
    }

    public void Callback_ReceiveStop(int userID)
    {
        foreach (var callback in callbacksForServer)
        {
            callback?.OnReceiveStop(userID);
        }
    }
    #endregion

    #region Setup Channel & Events
    public void SetChannel(Channel channel)
    {
        this.channel = channel;
    }

    public void BindNetworkEvent()
    {
        if (channel != null)
        {
            channel.BindInMainThread<UserData>(ApiEvent.RegisterUserEvent, OnRegisterUser);
            channel.BindInMainThread<int>(ApiEvent.UnregisterUserEvent, OnUnregisterUser);
            channel.BindInMainThread<int, string>(ApiEvent.ReciveConfig, OnReciveConfig);
            channel.BindInMainThread<int>(ApiEvent.ReadyEvent, OnReady);
            channel.BindInMainThread<int>(ApiEvent.QuitEvent, OnQuit);
            channel.BindInMainThread(ApiEvent.RequestStartEvent, OnRequestStart);
            channel.BindInMainThread(ApiEvent.RequestStopEvent, OnRequestStop);
            channel.BindInMainThread<int>(ApiEvent.ReceiveStartEvent, OnReceiveStart);
            channel.BindInMainThread<int>(ApiEvent.ReceiveStopEvent, OnReceiveStop);
        }
    }

    public void UnbindNetworkEvent()
    {
        if (channel != null)
        {
            channel.UnbindInMainThread<UserData>(ApiEvent.RegisterUserEvent, OnRegisterUser);
            channel.UnbindInMainThread<int>(ApiEvent.UnregisterUserEvent, OnUnregisterUser);
            channel.UnbindInMainThread<int, string>(ApiEvent.ReciveConfig, OnReciveConfig);
            channel.UnbindInMainThread<int>(ApiEvent.ReadyEvent, OnReady);
            channel.UnbindInMainThread<int>(ApiEvent.QuitEvent, OnQuit);
            channel.UnbindInMainThread(ApiEvent.RequestStartEvent, OnRequestStart);
            channel.UnbindInMainThread(ApiEvent.RequestStopEvent, OnRequestStop);
            channel.UnbindInMainThread<int>(ApiEvent.ReceiveStartEvent, OnReceiveStart);
            channel.UnbindInMainThread<int>(ApiEvent.ReceiveStopEvent, OnReceiveStop);
        }
    }

    public void SendOnConnectCallback()
    {
        Callback_OnConnect();
    }
    #endregion

    #region Receive Events
    private void OnRegisterUser(UserData userData)
    {
        Callback_RegisterUser(userData);
    }

    private void OnUnregisterUser(int userID)
    {
        Callback_UnregisterUser(userID);
    }

    private void OnReciveConfig(int userID, string json)
    {
        Callback_ReciveConfig(userID, json);
    }

    private void OnReady(int userID)
    {
        Callback_Ready(userID);
    }

    private void OnQuit(int userID)
    {
        Callback_Quit(userID);
    }

    private void OnRequestStart()
    {
        Callback_RequestStart();
    }

    private void OnRequestStop()
    {
        Callback_RequestStop();
    }

    private void OnReceiveStart(int userID)
    {
        Callback_ReceiveStart(userID);
    }

    private void OnReceiveStop(int userID)
    {
        Callback_ReceiveStop(userID);
    }
    #endregion

    #region Send Events
    public void RegisterApplication(UserData userData)
    {
        channel?.Trigger(ApiEvent.RegisterUserEvent, userData);
    }

    public void UnregisterApplication(int userID)
    {
        channel?.Trigger(ApiEvent.UnregisterUserEvent, userID);
    }

    public void SendApplicationConfig(int userID, string json)
    {
        channel?.Trigger(ApiEvent.ReciveConfig, userID, json);
    }

    public void ApplicationReady(int userID)
    {
        channel?.Trigger(ApiEvent.ReadyEvent, userID);
    }

    public void ApplicationQuit(int userID)
    {
        channel?.Trigger(ApiEvent.QuitEvent, userID);
    }

    public void RequestStart()
    {
        channel?.Trigger(ApiEvent.RequestStartEvent);
    }

    public void RequestStop()
    {
        channel?.Trigger(ApiEvent.RequestStopEvent);
    }

    public void ReceiveStart(int userID)
    {
        channel?.Trigger(ApiEvent.ReceiveStartEvent, userID);
    }

    public void ReceiveStop(int userID)
    {
        channel?.Trigger(ApiEvent.ReceiveStopEvent, userID);
    }
    #endregion

    #region Custom Bind/ Unbind /Trigger
    public void Bind(string eventName, Action action)
    {
        channel?.BindInMainThread(eventName, action);
    }

    public void Bind<T>(string eventName, Action<T> action)
    {
        channel?.BindInMainThread(eventName, action);
    }

    public void Bind<T1, T2>(string eventName, Action<T1, T2> action)
    {
        channel?.BindInMainThread(eventName, action);
    }

    public void Bind<T1, T2, T3>(string eventName, Action<T1, T2, T3> action)
    {
        channel?.BindInMainThread(eventName, action);
    }

    public void Bind<T1, T2, T3, T4>(string eventName, Action<T1, T2, T3, T4> action)
    {
        channel?.BindInMainThread(eventName, action);
    }

    public void Bind<T1, T2, T3, T4, T5>(string eventName, Action<T1, T2, T3, T4, T5> action)
    {
        channel?.BindInMainThread(eventName, action);
    }

    public void Bind<T1, T2, T3, T4, T5, T6>(string eventName, Action<T1, T2, T3, T4, T5, T6> action)
    {
        channel?.BindInMainThread(eventName, action);
    }

    public void Unbind(string eventName, Action action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T>(string eventName, Action<T> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T1, T2>(string eventName, Action<T1, T2> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T1, T2, T3>(string eventName, Action<T1, T2, T3> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T1, T2, T3, T4>(string eventName, Action<T1, T2, T3, T4> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T1, T2, T3, T4, T5>(string eventName, Action<T1, T2, T3, T4, T5> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T1, T2, T3, T4, T5, T6>(string eventName, Action<T1, T2, T3, T4, T5, T6> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Trigger(string eventName, params object[] datas)
    {
        channel?.Trigger(eventName, datas);
    }
    public void TriggerUdp(string eventName, params object[] datas)
    {
        channel?.TriggerUdp(eventName, datas);
    }
    #endregion
}
