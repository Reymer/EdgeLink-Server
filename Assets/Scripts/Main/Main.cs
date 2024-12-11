using UnityEngine;
using VARLive.ApexNetwork;
using DevKit.Console;
using static NetworkPortManager;
using System;
using System.Collections.Generic;

public class Main : MonoBehaviour, IApexNetworkCallback
{
    public struct PortData
    {
        public string ProtocolName; 
        public string NetProtocol; 
        public string LocalPort; 
        public string RemotePort; 
        public string TargetIP; 
        public bool IsConnected; 
        public int COMReceived; 
        public int NetReceived; 
        public string MaskType; 
    }

    private NetworkPortTableUIManager networkPortTableUIManager;
    private NetworkPortManager networkPortManager;  
    private static readonly string SyncReload = nameof(SyncReload);
    private static readonly string SyncGetData = nameof(SyncGetData);
    public Action On_Reload;
    private void Awake()
    {
        NetworkService.RegisterNetworkCallback(this);
        NetworkService.Connect("127.0.0.1", "IotServer");
        NetworkPortManager.Instance.Init();
    }

    private void Start()
    {
        networkPortTableUIManager = FindObjectOfType<NetworkPortTableUIManager>(true);
        Init();
    }

    private void Init()
    {
        networkPortTableUIManager.Init();
        BindAction();
    }

    private void BindAction()
    {
        On_Reload += Relaod;
    }

    private void Relaod()
    {
        var tempDatas = new List<PortData>();

        Debug.Log("收到重新整理的指令");
        var datas = NetworkPortManager.Instance.GetPortDatas();
        foreach( var data in datas ) 
        {
            var portData = new PortData
            {
                ProtocolName = data.ProtocolName,
                TargetIP = data.TargetIP,
                IsConnected = data.IsConnected,            
                RemotePort = data.RemotePortDetails.Port,
                LocalPort = data.LocalPortDetails.Port,
            };
            tempDatas.Add(portData);         
            Debug.Log($"名稱: {data.ProtocolName} 協定: {data.NetProtocol} 目標IP: {data.TargetIP} 遠端Port: {data.RemotePortDetails.Port} 本地Port: {data.LocalPortDetails.Port} 連線狀態: {data.IsConnected}");
        }

        NetworkService.NetworkSystem.LobbyChannel.Trigger(SyncGetData, tempDatas);
    }

    private void BindSymcEvent()
    {
        NetworkService.NetworkSystem.LobbyChannel.BindInMainThread(SyncReload, () => On_Reload?.Invoke());   
    }

    public void OnConnect()
    {
        ULog.Log("已連線");
        BindSymcEvent();
    }

    public void OnDisconnect()
    {
        ULog.Log("已斷線");
    }

    public void OnAddMember(User user)
    {
        Debug.Log($"使用者連線。使用者位址: {user.Address}, 使用者ID: {user.UserID}, 使用者名稱: {user.MachineName}");
    }

    public void OnRemoveMember(User user)
    {
        Debug.Log($"使用者斷線。使用者位址: {user.Address}, 使用者ID: {user.UserID}, 使用者名稱: {user.MachineName}");
    } 
    private void OnApplicationQuit()
    {
        NetworkPortManager.Instance.UnInit();
        networkPortTableUIManager.DeInit();
        NetworkService.UnregisterNetworkCallback(this);
        NetworkService.Disconnect();
    }
}
