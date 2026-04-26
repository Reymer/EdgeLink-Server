using DevKit;
using DevKit.Tool;
using System;
using UnityEngine;
using TMPro;
using DevKit.Console;
using System.Net.Sockets;
using System.Net;
using iotserver;
using Random = System.Random;
using System.Collections.Generic;
using System.Linq;

[DefaultExecutionOrder(-100)]
public class NetworkSettingsUI : MonoBehaviour
{
    #region 欄位

    private UICollector uiCollector;
    private ConsoleUI consoleUi;
    public event Action<PortData> Confirm;
    public event Action<PortData, PortData> EditConfirm;
    private bool isEditing = false;
    private PortData editingPortData = null;
    private string protocolType = "UDP";
    private string protocolName = string.Empty;
    private int? remotePort;
    private int? localPort;
    private string targetIP;
    private bool isOpenConsole = true;
    private string maskType = "original data";
    private string sourceProtocolName = string.Empty;
    private string sourceProtocolId = string.Empty;
    private TMP_Dropdown languageDropdown;
    private TMP_Dropdown sourceDropdown;
    #endregion

    #region Unity 生命週期

    private void Start()
    {
        RestoreLanguage();
        Init();
        Subscribe();
        SetUpLanguageDropdown();
        SetUpMaskTypeDropdown();
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

    private void OnDestroy()
    {
        Localization.Instance.LanguageChanged -= OnLanguageChangedExternally;
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
        sourceDropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_SourceProtocolDropdown);
        if (sourceDropdown != null)
            sourceDropdown.onValueChanged.AddListener(OnSourceDropdownValueChanged);
    }

    #region 多語系

    private const string LanguagePrefKey = "SelectedLanguageIndex";

    private void RestoreLanguage()
    {
        int savedIndex = PlayerPrefs.GetInt(LanguagePrefKey, 0);
        Localization.Instance.SetCurrentLanguage(savedIndex);
    }

    private void SetUpLanguageDropdown()
    {
        string[] shownNames = Localization.Instance.GetAllLanguageShownNames();
        languageDropdown.AddOptions(shownNames.ToList());
        languageDropdown.SetValueWithoutNotify(Localization.Instance.GetCurrentLanguageIndex());
        languageDropdown.onValueChanged.AddListener(OnUserChangeLanguage);
        Localization.Instance.LanguageChanged += OnLanguageChangedExternally;
    }

    private void OnLanguageChangedExternally()
    {
        languageDropdown.SetValueWithoutNotify(Localization.Instance.GetCurrentLanguageIndex());
        if (protocolType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase)
            && sourceDropdown != null && sourceDropdown.options.Count > 0)
        {
            sourceDropdown.options[0].text = Localization.Instance.GetText(LanguageKeys.UI_SourceNone);
            sourceDropdown.RefreshShownValue();
        }
    }

    private void OnUserChangeLanguage(int index)
    {
        PlayerPrefs.SetInt(LanguagePrefKey, index);
        PlayerPrefs.Save();
        Localization.Instance.SetCurrentLanguage(index);
    }

    #endregion

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

        // 打開對話框時刷新遮罩列表（以防有新協定註冊）
        RefreshMaskTypeDropdown();

        SetUiStatus(true, key);
    }

    private void Clear()
    {
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_NameInput).text = string.Empty;
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = string.Empty;
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = string.Empty;
        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_TargetIPInput).text = string.Empty;
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_NetProtocolDropdowm).SetValueWithoutNotify(0);
        uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_NetProtocolDropdowm).RefreshShownValue();
        var maskDropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_DropdownMask);
        maskDropdown.value = 0;
        maskDropdown.RefreshShownValue();
        remotePort = null;
        localPort = null;
        targetIP = null;
        protocolName = null;
        sourceProtocolName = string.Empty;
        sourceProtocolId = string.Empty;
        protocolType = "UDP";
        isEditing = false;
        editingPortData = null;
        if (sourceDropdown != null) sourceDropdown.SetValueWithoutNotify(0);
        OnDropdownValueChanged(0); // 最後套用 UDP 預設顯示（不重複設 value）
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

    private void SetMaskDropdownInteractable(bool interactable)
    {
        var dropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_DropdownMask);
        if (dropdown != null)
            dropdown.interactable = interactable;
    }


    #endregion

    #region 端口和 IP 管理

    /// <summary>
    /// 設置遮罩類型下拉選單（只在初始化時調用）
    /// </summary>
    private void SetUpMaskTypeDropdown()
    {
        RefreshMaskTypeDropdown();
    }

    /// <summary>
    /// 刷新遮罩類型下拉選單（使用 UILocalizeTMP_Dropdown 組件）
    /// 只在以下情況調用：
    /// 1. 初始化時
    /// 2. 用戶手動點擊刷新按鈕時
    /// 3. 打開新增端口對話框時
    /// 4. 協定列表變化時
    /// </summary>
    public void RefreshMaskTypeDropdown()
    {
        var dropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_DropdownMask);
        var localizeDropdown = dropdown.GetComponent<UILocalizeTMP_Dropdown>();

        if (localizeDropdown == null)
        {
            Debug.LogWarning("[NetworkSettingsUI] UILocalizeTMP_Dropdown component not found on Dropdown");
            return;
        }

        // 初始化組件（如果還沒初始化）
        localizeDropdown.Init();

        // 從 MaskTypeManager 獲取所有遮罩的本地化鍵
        var localizationKeys = MaskTypeManager.Instance.GetLocalizationKeys();

        // 調試信息

        // 保存當前選擇的索引
        int currentIndex = dropdown.value;

        // 更新 UILocalizeTMP_Dropdown 的本地化鍵
        localizeDropdown.SetKey(localizationKeys);

        // 嘗試恢復之前的選擇
        if (currentIndex >= 0 && currentIndex < localizationKeys.Length)
        {
            dropdown.value = currentIndex;
        }
        else
        {
            dropdown.value = 0;
        }

        dropdown.RefreshShownValue();
    }

    /// <summary>
    /// 獲取所有遮罩選項（供 Table 使用）
    /// </summary>
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

    #endregion

    #region 事件處理程序

    private void OnDropdownValueChanged(int index)
    {
        switch (index)
        {
            case 0: // UDP
                protocolType = "UDP";
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = string.Empty;
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = string.Empty;
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_TargetIPInput).text = string.Empty;
                SetUiStatus(false, UIKey.UI_TargetIPMask);
                SetUiStatus(false, UIKey.UI_RemotePortsMask);
                SetUiStatus(false, UIKey.UI_LocalPortsMask);
                SetUiStatus(false, UIKey.UI_SourceProtocolMask);
                SetMaskDropdownInteractable(true);
                break;
            case 1: // TCP Server
                protocolType = "TCP Server";
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = string.Empty;
                SetUiStatus(true, UIKey.UI_TargetIPMask);
                SetUiStatus(true, UIKey.UI_RemotePortsMask);
                SetUiStatus(false, UIKey.UI_LocalPortsMask);
                SetUiStatus(false, UIKey.UI_SourceProtocolMask);
                SetMaskDropdownInteractable(false);
                maskType = "OriginalData";
                uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_DropdownMask).value = 0;
                uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_DropdownMask).RefreshShownValue();
                break;
            case 2: // TCP Client
                protocolType = "TCP Client";
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = string.Empty;
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_TargetIPInput).text = string.Empty;
                SetUiStatus(false, UIKey.UI_TargetIPMask);
                SetUiStatus(false, UIKey.UI_RemotePortsMask);
                SetUiStatus(false, UIKey.UI_LocalPortsMask);
                SetUiStatus(true, UIKey.UI_SourceProtocolMask);
                SetMaskDropdownInteractable(true);
                RefreshSourceDropdown();
                break;
        }
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
        var maskIds = MaskTypeManager.Instance.GetMaskTypeIds();
        if (index >= 0 && index < maskIds.Count)
            maskType = maskIds[index];
    }

    private void RefreshSourceDropdown()
    {
        if (sourceDropdown == null) return;
        sourceDropdown.ClearOptions();

        var options = new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData(Localization.Instance.GetText(LanguageKeys.UI_SourceNone))
        };

        var allPorts = NetworkPortManager.Instance.GetAllPortDatas();
        foreach (var p in allPorts)
        {
            if (p.NetProtocol == "TCP Server")
            {
                string shortId = !string.IsNullOrEmpty(p.Id) ? " #" + p.Id[..8] : "";
                options.Add(new TMP_Dropdown.OptionData($"{p.ProtocolName}{shortId}"));
            }
        }

        sourceDropdown.AddOptions(options);
        sourceDropdown.value = 0;
        sourceDropdown.RefreshShownValue();
        sourceProtocolName = string.Empty;
    }

    private void OnSourceDropdownValueChanged(int index)
    {
        if (sourceDropdown == null || index == 0)
        {
            sourceProtocolName = string.Empty;
            sourceProtocolId = string.Empty;
            LogHelper.LogToConsole("[TCP Client] 未指定訊號來源，此 TCP Client 將不會接收任何轉發資料。", isError: false);
            return;
        }

        var allPorts = NetworkPortManager.Instance.GetAllPortDatas()
            .Where(p => p.NetProtocol == "TCP Server")
            .ToList();

        int portIndex = index - 1;
        if (portIndex >= 0 && portIndex < allPorts.Count)
        {
            sourceProtocolName = allPorts[portIndex].ProtocolName;
            sourceProtocolId = allPorts[portIndex].Id ?? string.Empty;
        }
        else
        {
            sourceProtocolName = string.Empty;
            sourceProtocolId = string.Empty;
        }
    }

    public void OpenForEdit(PortData portData)
    {
        isEditing = true;
        editingPortData = portData;

        RefreshMaskTypeDropdown();
        SetUiStatus(true, UIKey.UI_MenuRoot);

        uiCollector.GetAsset<TMP_InputField>(UIKey.UI_NameInput).text = portData.ProtocolName;
        protocolName = portData.ProtocolName;

        int protocolIndex = GetProtocolDropdownIndex(portData.NetProtocol);
        var protocolDropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_NetProtocolDropdowm);
        protocolDropdown.SetValueWithoutNotify(protocolIndex);
        protocolDropdown.RefreshShownValue();
        OnDropdownValueChanged(protocolIndex);

        switch (portData.NetProtocol)
        {
            case "UDP":
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = portData.RemotePortDetails?.Port ?? "";
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = portData.LocalPortDetails?.Port ?? "";
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_TargetIPInput).text = portData.TargetIP ?? "";
                remotePort = ParsePort(portData.RemotePortDetails?.Port);
                localPort = ParsePort(portData.LocalPortDetails?.Port);
                targetIP = portData.TargetIP;
                break;

            case "TCP Server":
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_LocalPortInput).text = portData.LocalPortDetails?.Port ?? "";
                localPort = ParsePort(portData.LocalPortDetails?.Port);
                break;

            case "TCP Client":
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_RemotePortInput).text = portData.RemotePortDetails?.Port ?? "";
                uiCollector.GetAsset<TMP_InputField>(UIKey.UI_TargetIPInput).text = portData.TargetIP ?? "";
                remotePort = ParsePort(portData.RemotePortDetails?.Port);
                targetIP = portData.TargetIP;
                if (!string.IsNullOrEmpty(portData.SourceProtocolId))
                    SelectSourceDropdownById(portData.SourceProtocolId);
                break;
        }

        SetMaskDropdownByMaskType(portData.MaskType);
    }

    private int GetProtocolDropdownIndex(string protocol)
    {
        return protocol switch
        {
            "TCP Server" => 1,
            "TCP Client" => 2,
            _ => 0  // UDP
        };
    }

    private void SelectSourceDropdownById(string id)
    {
        if (sourceDropdown == null || string.IsNullOrEmpty(id)) return;
        var allPorts = NetworkPortManager.Instance.GetAllPortDatas()
            .Where(p => p.NetProtocol == "TCP Server")
            .ToList();
        for (int i = 0; i < allPorts.Count; i++)
        {
            if (allPorts[i].Id == id)
            {
                sourceDropdown.SetValueWithoutNotify(i + 1);
                sourceProtocolName = allPorts[i].ProtocolName;
                sourceProtocolId = id;
                return;
            }
        }
    }


    private void SetMaskDropdownByMaskType(string maskTypeId)
    {
        var maskIds = MaskTypeManager.Instance.GetMaskTypeIds();
        int index = maskIds.IndexOf(maskTypeId);
        if (index < 0) index = 0;
        var maskDropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.UI_DropdownMask);
        maskDropdown.SetValueWithoutNotify(index);
        maskDropdown.RefreshShownValue();
        maskType = maskTypeId;
    }

    private void OnConfirm()
    {
        bool isAllEmpty = string.IsNullOrWhiteSpace(protocolName) &&
                          !remotePort.HasValue &&
                          !localPort.HasValue &&
                          (string.IsNullOrWhiteSpace(targetIP) || !IsValidIPv4(targetIP));

        if (isAllEmpty)
        {
            consoleUi.AddLog(Localization.Instance.GetText(LanguageKeys.Log_AllFieldsEmpty));
            return;
        }

        bool hasError = false;

        if (string.IsNullOrWhiteSpace(protocolName))
        {
            consoleUi.AddLog(Localization.Instance.GetText(LanguageKeys.Log_NameRequired));
            hasError = true;
        }

        if (protocolType.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            if (!remotePort.HasValue)
            {
                consoleUi.AddLog(Localization.Instance.GetText(LanguageKeys.Log_UdpNeedsRemotePort));
                hasError = true;
            }
        }
        else if (protocolType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase))
        {
            if (!remotePort.HasValue)
            {
                consoleUi.AddLog(Localization.Instance.GetText(LanguageKeys.Log_TcpClientNeedsRemotePort));
                hasError = true;
            }

            if (string.IsNullOrWhiteSpace(targetIP) || !IsValidIPv4(targetIP))
            {
                consoleUi.AddLog(Localization.Instance.GetText(LanguageKeys.Log_TcpClientNeedsIP));
                hasError = true;
            }
        }
        else if (protocolType.Equals("TCP Server", StringComparison.OrdinalIgnoreCase))
        {
            if (!localPort.HasValue)
            {
                consoleUi.AddLog(Localization.Instance.GetText(LanguageKeys.Log_TcpServerNeedsLocalPort));
                hasError = true;
            }
        }
        else
        {
            consoleUi.AddLog(Localization.Instance.GetText(LanguageKeys.Log_UnknownProtocolType));
            hasError = true;
        }

        if (hasError)
        {
            return;
        }

        // TCP Server: localPort → LocalPortDetails，RemotePortDetails = "--"
        // TCP Client: LocalPortDetails = "--"，remotePort → RemotePortDetails
        // UDP: localPort → LocalPortDetails，remotePort → RemotePortDetails
        string builtLocalPort = protocolType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase)
            ? "--"
            : localPort?.ToString();
        string builtRemotePort = protocolType.Equals("TCP Server", StringComparison.OrdinalIgnoreCase)
            ? "--"
            : remotePort?.ToString();

        var portData = new PortData
        {
            ProtocolName = protocolName,
            NetProtocol = protocolType,
            MaskType = maskType,
            LocalPortDetails = new PortDetails { Port = builtLocalPort },
            RemotePortDetails = new PortDetails { Port = builtRemotePort },
            TargetIP = targetIP,
            SourceProtocolName = protocolType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase) ? sourceProtocolName : string.Empty,
            SourceProtocolId = protocolType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase) ? sourceProtocolId : string.Empty,
        };

        if (isEditing)
        {
            var old = editingPortData;
            isEditing = false;
            editingPortData = null;
            EditConfirm?.Invoke(old, portData);
        }
        else
        {
            Confirm?.Invoke(portData);
        }
        CloseUi(UIKey.UI_MenuRoot);
    }

    #endregion
}
