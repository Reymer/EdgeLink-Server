using DevKit.Tool;
using System;
using UnityEngine;
using TMPro;
using DevKit.Console;
using System.Net.Sockets;
using System.Net;
using Random = System.Random;
using DevKit;
using UnityEngine.UI;

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

    private void Start()
    {
        Init();
        Subscribe();
    }

    private void Init()
    {
        consoleUi = GameObject.FindObjectOfType<ConsoleUI>(true);
        uiCollector = GetComponent<UICollector>();
        CloseMenu(UIKey.UI_MenuRoot);
    }

    private void Subscribe()
    {
        if (uiCollector == null) return;

        uiCollector.BindOnCheck(UIKey.UI_DeleteButton, () => CloseMenu(UIKey.UI_MenuRoot));
        uiCollector.BindOnCheck(UIKey.UI_MenuCancel, () => CloseMenu(UIKey.UI_MenuRoot));
        uiCollector.BindOnCheck(UIKey.UI_AddPort, () => OpenMenu(UIKey.UI_MenuRoot));
        uiCollector.BindOnCheck(UIKey.UI_OK, OnConfirm);
        uiCollector.BindOnCheck(UIKey.UI_Console, OnConsole);
        uiCollector.BindOnCheck(UIKey.UI_clear, OnClearConsole);
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_NetProtocolDropdowm).onValueChanged.AddListener(OnDropdownValueChanged);
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).onValueChanged.AddListener(OnRemotePortInput);
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).onValueChanged.AddListener(OnLocalPortInput);
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_TargetIPInput).onValueChanged.AddListener(OnTargetInput);
        uiCollector.GetAsset<InputField>(UIKey.UI_NameInput).onValueChanged.AddListener(OnNameInput);
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
        // 檢查是否所有輸入都是空的
        bool isAllEmpty = string.IsNullOrWhiteSpace(protocolName) &&
                          !remotePort.HasValue &&
                          !localPort.HasValue &&
                          (string.IsNullOrWhiteSpace(targetIP) || !IsValidIPv4(targetIP));

        if (isAllEmpty)
        {
            consoleUi.AddLog("尚未輸入，請檢查所有欄位。");
            return;
        }

        // 逐項檢查每一個欄位是否有輸入錯誤
        bool hasError = false;

        // 檢查 protocolName 是否為空
        if (string.IsNullOrWhiteSpace(protocolName))
        {
            consoleUi.AddLog("名稱不能為空，請檢查輸入。");
            hasError = true;
        }

        // 根據協議類型檢查對應的欄位
        if (protocolType.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            // 檢查 remotePort 是否已設置
            if (!remotePort.HasValue)
            {
                consoleUi.AddLog("UDP 協議需要遠程端口號，請檢查輸入。");
                hasError = true;
            }
        }
        else if (protocolType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase))
        {
            // 檢查 remotePort 和 targetIP 是否已設置
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
            // 檢查 localPort 是否已設置
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

        // 如果有錯誤，直接返回，不執行確認操作
        if (hasError)
        {
            return;
        }

        // 如果通過所有檢查，執行 Confirm
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

        // 成功後關閉菜單
        CloseMenu(UIKey.UI_MenuRoot);
    }




    private void OnConsole()
    {
        isOpenConsole = !isOpenConsole;
        SetUiStatus(isOpenConsole, UIKey.UI_ConsoleUI);
    }

    private void CloseMenu(string uiKey)
    {
        SetUiStatus(false, uiKey);
    }

    private void OpenMenu(string uiKey)
    {
        if (uiCollector.GetAsset<GameObject>(UIKey.UI_MenuRoot).activeSelf) { return; }
        Clear();
        SetUiStatus(true, uiKey);
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
