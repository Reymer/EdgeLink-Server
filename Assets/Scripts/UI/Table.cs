using DevKit.Tool;
using System;
using TMPro;
using UnityEngine;
using static NetworkPortManager;

public class Table : MonoBehaviour
{
    private UICollector uiCollector;
    private PortData currentPortData;
    private string maskType;
    public event Action<PortData> OnDelete;
    public event Action<PortData> OnConnect;
    public event Action<PortData> OnDisconnectedt;
    public event Action<PortData> OnMonitor;
    public event Action<PortData> OnMask;
    private NetworkSettingsUI networkSettingsUi;

    private void Start()
    {
        Subscribe();
    }

    public void Init(PortData portData)
    {
        uiCollector = GetComponent<UICollector>();
        networkSettingsUi = FindObjectOfType<NetworkSettingsUI>(true);
        currentPortData = portData;
        maskType = portData.MaskType; 
        UpdateUI(currentPortData); 
    }

    private void Subscribe()
    {
        uiCollector.BindOnCheck(UIKey.table_Delete, () => HandleAction(OnDelete));
        uiCollector.BindOnCheck(UIKey.table_Connect, () => HandleAction(OnConnect));
        uiCollector.BindOnCheck(UIKey.table_Disconnected, () => HandleAction(OnDisconnectedt));
        uiCollector.BindOnCheck(UIKey.table_Monitor, () => HandleAction(OnMonitor));

        var dropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.table_DropdownMask);
        dropdown.onValueChanged.AddListener(SetMaskType);
    }

    private void HandleAction(Action<PortData> action)
    {
        action?.Invoke(currentPortData);
    }

    private void UpdateUI(PortData portData)
    {
        SetValue(UIKey.table_nameText, portData.ProtocolName);
        SetValue(UIKey.table_prococolText, portData.NetProtocol);
        SetValue(UIKey.table_remoteText, portData.RemotePortDetails.Port);
        SetValue(UIKey.table_localPortText, portData.LocalPortDetails.Port);
        SetValue(UIKey.table_COMReceived, portData.COMReceived.ToString());
        SetValue(UIKey.table_netReceived, portData.NetReceived.ToString());
        SetValue(UIKey.table_ForwardTargetText, portData.TargetIP);
        SetValue(UIKey.table_netReceivedStatus, GetIsConnecting(portData));

        CreateMaskData();

        var dropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.table_DropdownMask);
        int index = GetIndexOfMaskType(maskType);
        dropdown.value = index; 
        dropdown.RefreshShownValue(); 
    }

    private void CreateMaskData()
    {
        var data = networkSettingsUi.GetAllOptions();
        var dropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.table_DropdownMask);

        dropdown.ClearOptions();
        dropdown.AddOptions(data);
    }

    private int GetIndexOfMaskType(string maskType)
    {
        var data = networkSettingsUi.GetAllOptions();
        for (int i = 0; i < data.Count; i++)
        {
            if (data[i].text == maskType)
            {
                return i;
            }
        }
        return 0;
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

    private void SetMaskType(int index)
    {
        var data = networkSettingsUi.GetAllOptions();

        if (index < 0 || index >= data.Count)
        {
            Debug.LogWarning($"Index {index} is out of bounds for available options.");
            return;
        }

        maskType = data[index].text;
        Debug.Log($"Mask type set to: {maskType}");

        currentPortData.MaskType = maskType;
        HandleAction(OnMask);
    }

    private void SetValue(string uiKey, string content)
    {
        uiCollector.SetText(uiKey, content);
    }
}
