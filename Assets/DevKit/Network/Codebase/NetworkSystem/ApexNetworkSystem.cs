using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VARLive.ApexNetwork;

public interface IApexNetworkCallback
{
    void OnConnect();
    void OnDisconnect();
    void OnAddMember(User user);
    void OnRemoveMember(User user);
}

public interface IChannelWorkCallback
{
    void BindNetworkEvent(Channel channel);

    void OnCreateChannel(Channel channel);
}

public class ApexNetworkSystem
{
    private const string DefaultChannelName = "Lobby";
    private const string PresenceStr = "presence-";

    private Apex apex;
    private PresenceChannel lobby;
    private readonly List<User> lobbyUsers = new();
    private readonly Dictionary<string, Channel> channels = new();
    private string channelName;

    #region Callback
    private readonly List<IApexNetworkCallback> networkCallbacks = new();
    private readonly List<IChannelWorkCallback> channelWorkCallbacks = new();

    public void RegisterNetworkCallback(IApexNetworkCallback callback)
    {
        if (networkCallbacks.Contains(callback) == false)
        {
            networkCallbacks.Add(callback);
        }
    }

    public void UnregisterNetworkCallback(IApexNetworkCallback callback)
    {
        if (networkCallbacks.Contains(callback))
        {
            networkCallbacks.Remove(callback);
        }
    }

    public void RegisterChannelWorkCallback(IChannelWorkCallback callback)
    {
        if (channelWorkCallbacks.Contains(callback) == false)
        {
            channelWorkCallbacks.Add(callback);
        }
    }

    public void UnregisterChannelWorkCallback(IChannelWorkCallback callback)
    {
        if (channelWorkCallbacks.Contains(callback))
        {
            channelWorkCallbacks.Remove(callback);
        }
    }

    private void Callback_OnConnect()
    {
        foreach (var callback in networkCallbacks)
        {
            callback.OnConnect();
        }
    }

    private void Callback_OnDisconnect()
    {
        //有人會在斷線的時候解註冊，ToArray防止操作錯誤的Error
        foreach (var callback in networkCallbacks.ToArray())
        {
            callback.OnDisconnect();
        }
    }

    private void Callback_OnAddMember(User user)
    {
        foreach (var callback in networkCallbacks)
        {
            callback.OnAddMember(user);
        }
    }

    private void Callback_OnRemoveMember(User user)
    {
        foreach (var callback in networkCallbacks)
        {
            callback.OnRemoveMember(user);
        }
    }

    private void Callback_OnCreateChannel(string channelName)
    {
        //foreach (var callback in networkCallbacks)
        //{
        //    callback.OnCreateChannel(channelName);
        //}
    }

    private void Callback_OnCreateChannel(Channel channel)
    {
        foreach (var callback in channelWorkCallbacks)
        {
            callback.OnCreateChannel(channel);
        }
    }

    private void Callback_BindNetworkEvent(Channel channel)
    {
        foreach (var callback in channelWorkCallbacks)
        {
            callback.BindNetworkEvent(channel);
        }
    }
    #endregion

    /// <summary>
    /// 本地玩家
    /// </summary>
    public User LocalUser
    {
        get
        {
            if (apex == null)
                return null;
            else
                return apex.me;
        }
    }

    /// <summary>
    /// 所有玩家
    /// </summary>
    public List<User> Users
    {
        get
        {
            return lobbyUsers;
        }
    }

    /// <summary>
    /// 大廳提供的網路頻道
    /// </summary>
    public Channel LobbyChannel
    {
        get
        {
            return lobby;
        }
    }

    /// <summary>
    /// 是否已連線
    /// </summary>
    public bool IsConnected
    {
        get
        {
            if (apex != null)
            {
                return apex.IsConnected();
            }
            return false;
        }
    }

    /// <summary>
    /// 是否正在連線
    /// </summary>
    public bool IsConnecting
    {
        get
        {
            if (apex != null)
            {
                return apex.IsConnecting();
            }
            return false;
        }
    }

    public ApexNetworkSystem()
    {
    }

    public ApexNetworkSystem(string ipAddress, string appKey)
    {
        Init(ipAddress, appKey);
    }

    public ApexNetworkSystem(string ipAddress, string appKey, string playerName)
    {
        Init(ipAddress, appKey, playerName);
    }

    public void Init(string ipAddress, string appKey)
    {
        Init(ipAddress, appKey, string.Empty);
    }

    public void Init(string ipAddress, string appKey, string playerName)
    {
        apex = new Apex(ipAddress, appKey, playerName: playerName);
        apex.RegisterOnConnectEvent(OnConnect);
        apex.RegisterOnDisconnectEvent(OnDisconnect);
    }

    public void Connect()
    {
        Connect(DefaultChannelName);
    }

    public void Connect(string channelName)
    {
        this.channelName = $"{PresenceStr}{channelName}";
        apex?.Connect();
    }

    public void Disconnect()
    {
        if (apex != null)
        {
            if (lobby != null)
            {
                lobby.UnregisterOnSubscribeEvent(OnJoinLobby);
                apex.Unsubscribe(lobby);
            }
            if (apex.IsConnected())
            {
                apex.Disconnect();
            }
        }
    }

    private void OnConnect()
    {
        Debug.Log("OnConnect");

        apex.StartUdp();
        lobby = apex.CreateChannel(channelName) as PresenceChannel;
        lobby.RegisterOnSubscribeEvent(OnJoinLobby);
        lobby.RegisterOnAddMemeberEvent(OnAddMember);
        lobby.RegisterOnRemoveMemeberEvent(OnRemoveMember);
        Callback_BindNetworkEvent(lobby);
        apex.Subscribe(lobby);
    }

    private void OnDisconnect(Exception obj)
    {
        Callback_OnDisconnect();

        //foreach (var channel in channels.Values)
        //{
        //    apex.Unsubscribe(channel);
        //}
        channels.Clear();
        //apex.Unsubscribe(lobby);

        lobbyUsers.Clear();
    }

    private void OnJoinLobby()
    {
        Callback_OnConnect();

        foreach (var member in lobby.members)
        {
            OnAddMember(member);
        }
    }

    private void OnAddMember(User user)
    {
        if (lobbyUsers.Contains(user) == false)
        {
            lobbyUsers.Add(user);

            Callback_OnAddMember(user);
        }
    }

    private void OnRemoveMember(User user)
    {
        if (lobbyUsers.Contains(user))
        {
            lobbyUsers.Remove(user);

            Callback_OnRemoveMember(user);
        }
    }

    public bool IsMe(int userID)
    {
        if (LocalUser != null)
        {
            return userID == LocalUser.UserID;
        }
        return false;
    }

    public User GetUser(int userID)
    {
        if (lobbyUsers != null)
            return lobbyUsers.Find(p => p.UserID == userID);
        else
            return null;
    }

    public void CreateChannel(string channelName)
    {
        if (channels.ContainsKey(channelName) == false)
        {
            string name = $"{PresenceStr}{channelName}";
            var newChannel = apex.CreateChannel(name) as PresenceChannel;
            newChannel.RegisterOnSubscribeEvent(() => OnChannelCreated(channelName));
            newChannel.RegisterOnSubscribeEvent(() => OnChannelCreated(newChannel));
            Callback_BindNetworkEvent(newChannel);
            apex.Subscribe(newChannel);
            channels.Add(channelName, newChannel);
        }
    }

    private void OnChannelCreated(string channelName)
    {
        Callback_OnCreateChannel(channelName);
    }

    private void OnChannelCreated(Channel channel)
    {
        Callback_OnCreateChannel(channel);
    }

    public Channel GetChannel(string channelName)
    {
        if (channels.ContainsKey(channelName))
        {
            return channels[channelName];
        }
        return null;
    }
}
