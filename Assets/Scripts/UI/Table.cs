using DevKit.Tool;
using System;
using Unity.VisualScripting;
using UnityEngine;
using static NetworkPortManager;

public class Table : MonoBehaviour
{
    public UICollector uiCollector;

    private string protocolType;
    private string remotePort;
    private string localPort;
    private string targetIP;
    private bool receivedStatus;
    private int receivedCount;
    private int comReceivedCount;

    public event Action<PortData> OnDelete;
    public event Action<PortData> OnConnect;
    public event Action<PortData> OnDisconnectedt;

    private void Start()
    {
        Subscribe();
    }

    public void Init(PortData portData)
    {
        this.protocolType = portData.NetProtocol;
        this.remotePort = portData.RemotePortDetails.Port;
        this.localPort = portData.LocalPortDetails.Port;
        this.targetIP = portData.TargetIP;
        this.comReceivedCount = portData.COMReceived;
        this.receivedCount = portData.NetReceived;
        this.receivedStatus = portData.IsConnected;
        UpdateUI(portData);
    }

    private void Subscribe()
    {
        uiCollector.BindOnCheck(UIKey.table_Delete, () => HandleAction(OnDelete));
        uiCollector.BindOnCheck(UIKey.table_Connect, () => HandleAction(OnConnect));
        uiCollector.BindOnCheck(UIKey.table_Disconnected, () => HandleAction(OnDisconnectedt));       
    }

    private void HandleAction(Action<PortData> action)
    {
        action?.Invoke(CreatePortData());
    }

    private void UpdateUI(PortData portData)
    {
        SetValue(UIKey.table_prococolText, protocolType);
        SetValue(UIKey.table_remoteText, remotePort);
        SetValue(UIKey.table_localPortText, localPort);
        SetValue(UIKey.table_COMReceived, comReceivedCount.ToString());
        SetValue(UIKey.table_netReceived, receivedCount.ToString());
        SetValue(UIKey.table_ForwardTargetText, targetIP);
        SetValue(UIKey.table_netReceivedStatus, GetIsConnecting(portData));
    }

    private string GetIsConnecting(PortData portData)
    {
        if (portData.NetProtocol.Equals("TCP Client") || portData.NetProtocol.Equals("TCP Server") || portData.NetProtocol.Equals("UDP"))
        {
            if (portData.IsConnected)
            {
                uiCollector.Deactive(UIKey.table_ConnectRoot);
                uiCollector.Active(UIKey.table_DisconnectedRoot);
                return "Connecting";
            }
            else
            {
                uiCollector.Active(UIKey.table_ConnectRoot);
                uiCollector.Deactive(UIKey.table_DisconnectedRoot);
                return "Not Connecting";
            }
        }
        return "Not Connecting";
    }

    private void SetValue(string uiKey, string content)
    {
        uiCollector.SetText(uiKey, content);
    }

    private PortData CreatePortData()
    {
        return new PortData
        {
            NetProtocol = protocolType,
            RemotePortDetails = new PortDetails { Port = remotePort },
            LocalPortDetails = new PortDetails { Port = localPort },
            TargetIP = targetIP,
            IsConnected = receivedStatus,
            COMReceived = comReceivedCount,
            NetReceived = receivedCount,
            OnUpdate = null
        };
    }
}
