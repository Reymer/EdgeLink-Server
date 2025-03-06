using DevKit.Tool;
using System;
using UnityEngine;
using TMPro;
using DevKit.Console;
using System.Net.Sockets;
using System.Net;
using Random = System.Random;
using UnityEngine.UI;
using System.Collections.Generic;
using static NetworkPortManager;
using System.Linq;
using DevKit;

public class NetworkSettingsUI : MonoBehaviour
{
    #region 欄位

    private UICollector uiCollector;
    private ConsoleUI consoleUi;
    public event Action<PortData> Confirm;
    private string protocolType = "UDP";
    private string protocolName = string.Empty;
    private int? remotePort;
    private int? localPort;
    private string targetIP;
    private bool isOpenConsole = true;
    private string maskType = "original data";
    private TMP_Dropdown languageDropdown;
    #endregion

    #region Unity 生命週期

    private void Start()
    {
        Init();
        Subscribe();
        SetUpLanguageDropdown();
    }

    private void Update()
    {
        Transform parentTransform = uiCollector.GetAsset<GameObject>(UIKey.UI_Consolelayout).transform;
        int childCount = parentTransform.childCount;
        if (childCount >= 50)
        {
            OnClearConsole();
        }
    }

    #endregion

    #region 初始化

    private void Init()
    {
        consoleUi = GameObject.FindObjectOfType<ConsoleUI>(true);
        uiCollector = GetComponent<UICollector>();
        InitMenu();
    }

    private void InitMenu()
    {
        CloseUi(UIKey.UI_MenuRoot);
    }

    private void Subscribe()
    {
        if (uiCollector == null) return;

        uiCollector.BindOnCheck(UIKey.UI_DeleteButton, () => CloseUi(UIKey.UI_MenuRoot));
        uiCollector.BindOnCheck(UIKey.UI_MenuCancel, () => CloseUi(UIKey.UI_MenuRoot));
        uiCollector.BindOnCheck(UIKey.UI_AddPort, () => OpenMenu(UIKey.UI_MenuRoot));
        uiCollector.BindOnCheck(UIKey.UI_OK, OnConfirm);
        uiCollector.BindOnCheck(UIKey.UI_Console, OnConsole);
        uiCollector.BindOnCheck(UIKey.UI_clear, OnClearConsole);
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_NetProtocolDropdowm).onValueChanged.AddListener(OnDropdownValueChanged);
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).onValueChanged.AddListener(OnRemotePortInput);
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).onValueChanged.AddListener(OnLocalPortInput);
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_TargetIPInput).onValueChanged.AddListener(OnTargetInput);
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_NameInput).onValueChanged.AddListener(OnNameInput);
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_DropdownMask).onValueChanged.AddListener(OnMaskDropdownValueChanged);
        languageDropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_LanguageDropdown);
    }
    private void SetUpLanguageDropdown()
    {
        string[] shownNames = Localization.Instance.GetAllLanguageShownNames();
        languageDropdown.AddOptions(shownNames.ToList());
        languageDropdown.onValueChanged.AddListener(OnUserChangeLanguage);
    }
    private void OnUserChangeLanguage(int index)
    {
        Localization.Instance.SetCurrentLanguage(index);
    }

    #endregion

    #region UI 管理

    private void CloseUi(string uiKey)
    {
        SetUiStatus(false, uiKey);
    }

    private void OpenMenu(string key)
    {
        if (uiCollector.GetAsset<GameObject>(key).activeSelf) return;
        Clear();
        SetUiStatus(true, key);
    }

    private void Clear()
    {
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_NameInput).text = string.Empty;
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = string.Empty;
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = string.Empty;
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_TargetIPInput).text = string.Empty;
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_NetProtocolDropdowm).value = 0;
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_NetProtocolDropdowm).RefreshShownValue();
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_DropdownMask).value = 0;
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_DropdownMask).RefreshShownValue();
        remotePort = null;
        localPort = null;
        targetIP = null;
        protocolName = null;
        protocolType = "UDP";
        SetUiStatus(false, UIKey.UI_TargetIPMask);
        SetUiStatus(false, UIKey.UI_RemotePortsMask);
        SetUiStatus(false, UIKey.UI_LocalPortsMask);
    }

    private void OnConsole()
    {
        isOpenConsole = !isOpenConsole;
        SetUiStatus(isOpenConsole, UIKey.UI_ConsoleUI);
    }

    private void SetUiStatus(bool status, string uiKey)
    {
        if (uiCollector != null)
        {
            uiCollector.SetActive(uiKey, status);
        }
    }

    #endregion

    #region 端口和 IP 管理

    public List<TMP_Dropdown.OptionData> GetAllOptions()
    {
        var dropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_DropdownMask);
        return dropdown.options;
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

    #endregion

    #region 事件處理程序

    private void OnDropdownValueChanged(int index)
    {
        switch (index)
        {
            case 0:
                protocolType = "UDP";
                SetUiStatus(false, UIKey.UI_TargetIPMask);
                SetUiStatus(false, UIKey.UI_RemotePortsMask);
                SetUiStatus(false, UIKey.UI_LocalPortsMask);
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = string.Empty;
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = string.Empty;
                break;
            case 1:
                protocolType = "TCP Server";
                var randomRemotePort = GetRandomAvailablePort().ToString();
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = randomRemotePort;
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = string.Empty;
                SetUiStatus(true, UIKey.UI_TargetIPMask);
                SetUiStatus(true, UIKey.UI_RemotePortsMask);
                SetUiStatus(false, UIKey.UI_LocalPortsMask);
                break;
            case 2:
                protocolType = "TCP Client";
                var randomLocalPort = GetRandomAvailablePort().ToString();
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = randomLocalPort;
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

    private void OnMaskDropdownValueChanged(int index)
    {
        switch (index)
        {
            case 0:
                maskType = "original data";
                break;
            case 1:
                maskType = "Robot to 10";
                break;
            case 2:
                maskType = "Robot to 16";
                break;
        }
        Debug.Log(maskType);
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

        var portData = new PortData
        {
            ProtocolName = protocolName,
            NetProtocol = protocolType,
            MaskType = maskType,
            LocalPortDetails = new PortDetails { Port = protocolType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase) ? "--" : localPort?.ToString() },
            RemotePortDetails = new PortDetails { Port = protocolType.Equals("TCP Server", StringComparison.OrdinalIgnoreCase) ? "--" : remotePort?.ToString() },
            TargetIP = targetIP
        };

        Confirm?.Invoke(portData);
        CloseUi(UIKey.UI_MenuRoot);
    }

    #endregion
}
