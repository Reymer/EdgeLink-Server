using DevKit.Tool;
using System;
using UnityEngine;
using TMPro;
using DevKit.Console;
using System.Net.Sockets;
using System.Net;
using Random = System.Random;
using UnityEngine.UI;
using static DataPacketController;
using static UnityEngine.UI.Dropdown;

public class NetworkSettingsUI : MonoBehaviour
{
    private UICollector uiCollector;
    private ConsoleUI consoleUi;
    public event Action<string, string, string, string, string> Confirm;
    private string protocolType = "UDP";
    private string protocolName = string.Empty;
    private int? remotePort;
    private int? localPort;
    private string targetIP;
    private bool isOpenConsole = true;
    private string packetStartMarker = string.Empty;
    private string packetEndMarker = string.Empty;
    private string packetDataName = string.Empty;
    private int maxPacketSize;
    private bool useMask = false;
    private DataPacketController packetController;
    private void Start()
    {
        Init();
        Subscribe();
    }

    private void Init()
    {
        consoleUi = GameObject.FindObjectOfType<ConsoleUI>(true);
        uiCollector = GetComponent<UICollector>();
        packetController = new DataPacketController();
        InitMenu();
        InitMaskEvent();

    }

    private void InitMenu()
    {
        CloseUi(UIKey.UI_MenuRoot);
    }

    private void InitMaskEvent()
    {
        CloseUi(UIKey.UI_MaskRoot);
        CloseUi(UIKey.UI_MaskDropdown);
        uiCollector.GetAsset<Toggle>(UIKey.UI_MaskToggle).isOn = false;
    }

    private void Subscribe()
    {
        if (uiCollector == null) return;

        uiCollector.BindOnCheck(UIKey.UI_DeleteButton, () => CloseUi(UIKey.UI_MenuRoot));
        uiCollector.BindOnCheck(UIKey.UI_MaskDeleteButton, () => CloseUi(UIKey.UI_MaskRoot));
        uiCollector.BindOnCheck(UIKey.UI_MenuCancel, () => CloseUi(UIKey.UI_MenuRoot));
        uiCollector.BindOnCheck(UIKey.UI_MaskCancel, () => CloseUi(UIKey.UI_MaskRoot));
        uiCollector.BindOnCheck(UIKey.UI_Mask, () => OpenMenu(UIKey.UI_MaskRoot));
        uiCollector.BindOnCheck(UIKey.UI_AddPort, () => OpenMenu(UIKey.UI_MenuRoot));
        uiCollector.BindOnCheck(UIKey.UI_MaskOK, OnMaskSetting);
        uiCollector.BindOnCheck(UIKey.UI_OK, OnConfirm);
        uiCollector.BindOnCheck(UIKey.UI_Console, OnConsole);
        uiCollector.BindOnCheck(UIKey.UI_clear, OnClearConsole);
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_NetProtocolDropdowm).onValueChanged.AddListener(OnDropdownValueChanged);
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_MaskDropdown).onValueChanged.AddListener(OnDropdownValueChanged);
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).onValueChanged.AddListener(OnRemotePortInput);
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).onValueChanged.AddListener(OnLocalPortInput);
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_TargetIPInput).onValueChanged.AddListener(OnTargetInput);

        uiCollector.GetAsset<InputField>(UIKey.UI_PacketDataNameInput).onValueChanged.AddListener(OnPacketDataNameInput);
        uiCollector.GetAsset<InputField>(UIKey.UI_PacketStartMarkerInput).onValueChanged.AddListener(OnPacketStartMarkerInput);
        uiCollector.GetAsset<InputField>(UIKey.UI_PacketEndMarkerInput).onValueChanged.AddListener(OnPacketEndMarkerInput);
        uiCollector.GetAsset<InputField>(UIKey.UI_MaxPacketSizeInput).onValueChanged.AddListener(OnMaxPacketSizeInput);



        uiCollector.GetAsset<InputField>(UIKey.UI_NameInput).onValueChanged.AddListener(OnNameInput);
        uiCollector.GetAsset<Toggle>(UIKey.UI_MaskToggle).onValueChanged.AddListener(OnMaskDrodown);
    }

    private void OnMaxPacketSizeInput(string value)
    {
        if (int.TryParse(value, out int size) && size > 0)
        {
            maxPacketSize = size;
        }
        else
        {
            consoleUi.AddLog("最大封包大小必須是正整數。");
        }
    }

    private void OnPacketEndMarkerInput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            consoleUi.AddLog("結束標記不能為空。");
        }
        else
        {
            packetEndMarker = value;
        }
    }

    private void OnPacketStartMarkerInput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            consoleUi.AddLog("開始標記不能為空。");
        }
        else
        {
            packetStartMarker = value;
        }
    }

    private void OnPacketDataNameInput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            consoleUi.AddLog("數據包名稱不能為空。");
        }
        else
        {
            packetDataName = value;
        }
    }

    private void OnMaskSetting()
    {
        var dropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_MaskDropdown);

        // 確保在創建數據包之前所有屬性都合法
        if (string.IsNullOrWhiteSpace(packetDataName))
        {
            consoleUi.AddLog("數據包名稱不能為空，請檢查輸入。");
            return;
        }

        if (string.IsNullOrWhiteSpace(packetStartMarker))
        {
            consoleUi.AddLog("開始標記不能為空，請檢查輸入。");
            return;
        }

        if (string.IsNullOrWhiteSpace(packetEndMarker))
        {
            consoleUi.AddLog("結束標記不能為空，請檢查輸入。");
            return;
        }

        if (maxPacketSize <= 0)
        {
            consoleUi.AddLog("最大封包大小必須是正整數，請檢查輸入。");
            return;
        }

        // 創建數據包並更新下拉選單
        var dataPacket = new DataPacket
        {
            dataPacketName = this.packetDataName,
            packetStartMarker = this.packetStartMarker,
            packetEndMarker = this.packetEndMarker,
            maxPacketSize = maxPacketSize
        };

        packetController.CreateDataPacket(dataPacket);
        Debug.Log($"dataPacketName: {dataPacket.dataPacketName}, packetStartMarker: {dataPacket.packetStartMarker}, packetEndMarker: {dataPacket.packetEndMarker}, maxPacketSize: {dataPacket.maxPacketSize}");

        var optionData = new TMP_Dropdown.OptionData
        {
            text = this.packetDataName
        };

        dropdown.options.Add(optionData);
        dropdown.value = dropdown.options.Count - 1;
        dropdown.RefreshShownValue();
        CloseUi(UIKey.UI_MaskRoot);
    }


    private void OnMaskDropdownValueChanged(int index)
    {

    }


    private void OnMaskDrodown(bool status)
    {
        if (status) 
        {
            uiCollector.SetActive(UIKey.UI_MaskDropdown, true);
            useMask = true;
        }
        else
        {
            uiCollector.SetActive(UIKey.UI_MaskDropdown, false);
            useMask = false;
        }
    }

    private void Update()
    {
        Transform parentTransform = uiCollector.GetAsset<GameObject>(UIKey.UI_Consolelayout).transform;
        int childCount = parentTransform.childCount;
        if (childCount < 50)
        {
            return;
        }
        OnClearConsole();
    }

    private void OnClearConsole()
    {
        Transform parentTransform = uiCollector.GetAsset<GameObject>(UIKey.UI_Consolelayout).transform;

        if (parentTransform == null) return;
        int childCount = parentTransform.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            Transform child = parentTransform.GetChild(i);
            Destroy(child.gameObject);
        }
    }

    private void OnDropdownValueChanged(int index)
    {
        switch (index)
        {
            case 0:
                protocolType = "UDP";
                SetUiStatus(true, UIKey.UI_TargetIPMask);
                SetUiStatus(false, UIKey.UI_RemotePortsMask);
                SetUiStatus(false, UIKey.UI_LocalPortsMask);
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = string.Empty;
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = string.Empty;
                break;
            case 1:
                protocolType = "TCP Server";
                var remotePort = GetRandomAvailablePort().ToString();
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = remotePort;
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = string.Empty;
                SetUiStatus(true, UIKey.UI_TargetIPMask);
                SetUiStatus(true, UIKey.UI_RemotePortsMask);
                SetUiStatus(false, UIKey.UI_LocalPortsMask);
                break;
            case 2:
                protocolType = "TCP Client";
                var localPort = GetRandomAvailablePort().ToString();
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = localPort;
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = string.Empty;
                SetUiStatus(false, UIKey.UI_TargetIPMask);
                SetUiStatus(false, UIKey.UI_RemotePortsMask);
                SetUiStatus(true, UIKey.UI_LocalPortsMask);
                break;
        }
        Debug.Log(protocolType);
    }
    private void OnNameInput(string name)
    {
        protocolName = name;
    }

    private void OnRemotePortInput(string value)
    {
        remotePort = ParsePort(value);
    }

    private void OnTargetInput(string value)
    {
        targetIP = value;
    }

    private void OnLocalPortInput(string value)
    {
        localPort = ParsePort(value);
    }

    private int? ParsePort(string portString)
    {
        if (int.TryParse(portString, out int port) && port > 0 && port <= 65535)
        {
            return port;
        }
        return null;
    }

    private bool IsValidIPv4(string ipString)
    {
        if (string.IsNullOrWhiteSpace(ipString)) return false;

        string[] parts = ipString.Split('.');
        if (parts.Length != 4) return false;

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out int number) || number < 0 || number > 255)
            {
                return false;
            }
        }

        return true;
    }

    private void OnConfirm()
    {
        bool isAllEmpty = string.IsNullOrWhiteSpace(protocolName) &&
                          !remotePort.HasValue &&
                          !localPort.HasValue &&
                          (string.IsNullOrWhiteSpace(targetIP) || !IsValidIPv4(targetIP));

        if (isAllEmpty)
        {
            consoleUi.AddLog("尚未輸入，請檢查所有欄位。");
            return;
        }

        bool hasError = false;

        if (string.IsNullOrWhiteSpace(protocolName))
        {
            consoleUi.AddLog("名稱不能為空，請檢查輸入。");
            hasError = true;
        }

        if (protocolType.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            if (!remotePort.HasValue)
            {
                consoleUi.AddLog("UDP 協議需要遠程端口號，請檢查輸入。");
                hasError = true;
            }
        }
        else if (protocolType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase))
        {
            if (!remotePort.HasValue)
            {
                consoleUi.AddLog("TCP Client 需要遠程端口號，請檢查輸入。");
                hasError = true;
            }

            if (string.IsNullOrWhiteSpace(targetIP) || !IsValidIPv4(targetIP))
            {
                consoleUi.AddLog("TCP Client 需要有效的目標 IP 地址，請檢查輸入。");
                hasError = true;
            }
        }
        else if (protocolType.Equals("TCP Server", StringComparison.OrdinalIgnoreCase))
        {
            if (!localPort.HasValue)
            {
                consoleUi.AddLog("TCP Server 需要本地端口號，請檢查輸入。");
                hasError = true;
            }
        }
        else
        {
            consoleUi.AddLog("未知的協議類型，請檢查選擇。");
            hasError = true;
        }
        if (hasError)
        {
            return;
        }

        if (useMask)
        {

        }


        if (protocolType.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            Confirm?.Invoke(protocolName, protocolType, remotePort.ToString(), localPort?.ToString(), string.Empty);
        }
        else if (protocolType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase))
        {
            Confirm?.Invoke(protocolName, protocolType, remotePort.ToString(), "--", targetIP);
        }
        else if (protocolType.Equals("TCP Server", StringComparison.OrdinalIgnoreCase))
        {
            Confirm?.Invoke(protocolName, protocolType, "--", localPort.ToString(), targetIP);
        }
        CloseUi(UIKey.UI_MenuRoot);
    }




    private void OnConsole()
    {
        isOpenConsole = !isOpenConsole;
        SetUiStatus(isOpenConsole, UIKey.UI_ConsoleUI);
    }

    private void CloseUi(string uiKey)
    {
        SetUiStatus(false, uiKey);
    }

    private void OpenMenu(string Key)
    {
        if (uiCollector.GetAsset<GameObject>(Key).activeSelf) { return; }
        Clear();
        SetUiStatus(true, Key);
    }

    private void Clear()
    {
        uiCollector.GetAsset<InputField>(UIKey.UI_NameInput).text = string.Empty;
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = string.Empty;
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = string.Empty;
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_TargetIPInput).text = string.Empty;
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_NetProtocolDropdowm).value = 0;
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_NetProtocolDropdowm).RefreshShownValue();
        remotePort = null;
        localPort = null;
        targetIP = null;
        protocolName = null;
        protocolType = "UDP";
        SetUiStatus(true, UIKey.UI_TargetIPMask);
        SetUiStatus(false, UIKey.UI_RemotePortsMask);
        SetUiStatus(false, UIKey.UI_LocalPortsMask);
        InitMaskEvent();
    }

    private void SetUiStatus(bool status, string uiKey)
    {
        if (uiCollector != null)
        {
            uiCollector.SetActive(uiKey, status);
        }
    }

    private int GetRandomAvailablePort()
    {
        var random = new Random();
        int port;

        while (true)
        {
            port = random.Next(49152, 65535);
            if (IsPortAvailable(port))
            {
                break;
            }
        }

        return port;
    }

    private bool IsPortAvailable(int port)
    {
        bool isAvailable = true;

        try
        {
            TcpListener listener = new(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
        }
        catch (SocketException)
        {
            isAvailable = false;
        }

        return isAvailable;
    }
}
