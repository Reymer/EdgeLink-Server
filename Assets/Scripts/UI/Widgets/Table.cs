using DevKit;
using DevKit.Tool;
using System;
using TMPro;
using UnityEngine;

public class Table : MonoBehaviour
{
    #region 欄位

    public UICollector uiCollector;
    private PortData currentPortData;
    private string maskType;
    public event Action<PortData> OnDelete;
    public event Action<PortData> OnConnect;
    public event Action<PortData> OnDisconnectedt;
    public event Action<PortData> OnMonitor;
    public event Action<PortData> OnMask;
    private NetworkSettingsUI networkSettingsUi;

    #endregion

    #region 初始化

    public void Init(PortData portData)
    {
        Subscribe();
        networkSettingsUi = FindObjectOfType<NetworkSettingsUI>(true);
        currentPortData = portData;
        maskType = portData.MaskType;
        UpdateUI(currentPortData);
    }

    #endregion

    #region 訂閱事件

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

    #endregion

    #region UI 更新

    /// <summary>
    /// 完整更新 UI（初始化時調用）
    /// </summary>
    private void UpdateUI(PortData portData)
    {
        // 靜態數據（不常變）
        SetValue(UIKey.table_nameText, portData.ProtocolName);
        SetValue(UIKey.table_prococolText, portData.NetProtocol);
        SetValue(UIKey.table_remoteText, portData.RemotePortDetails.Port);
        SetValue(UIKey.table_localPortText, portData.LocalPortDetails.Port);
        SetValue(UIKey.table_ForwardTargetText, portData.TargetIP);
        CreateMaskData();

        // ✅ 根據 portData.MaskType 設置正確的索引
        var dropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.table_DropdownMask);
        int index = GetIndexOfMaskType(maskType);
        dropdown.value = index;
        dropdown.RefreshShownValue();

        // 動態數據
        UpdateDynamicUI(portData);
    }

    /// <summary>
    /// 只更新動態 UI（連接狀態、數據量等）
    /// 這個方法不會打斷用戶的下拉選單操作
    /// </summary>
    public void UpdateDynamicUI(PortData portData)
    {
        // 更新 PortData 引用（可能已變化）
        currentPortData = portData;

        // 只更新會變化的數據
        SetValue(UIKey.table_COMReceived, portData.COMReceived.ToString());
        SetValue(UIKey.table_netReceived, portData.NetReceived.ToString());

        // 更新連接狀態
        var localize_Text = uiCollector.GetAsset<UILocalizeTMP_Text>(UIKey.table_netReceivedStatusRoot);
        localize_Text.SetKey(GetIsConnecting(portData));
    }

    private void CreateMaskData()
    {
        var dropdown = uiCollector.GetAsset<TMP_Dropdown>(UIKey.table_DropdownMask);
        
        if (dropdown.TryGetComponent<UILocalizeTMP_Dropdown>(out var localizeDropdown))
        {
            // 初始化組件（如果還沒初始化）
            localizeDropdown.Init();

            // 從 MaskTypeManager 獲取所有遮罩的本地化鍵
            var localizationKeys = MaskTypeManager.Instance.GetLocalizationKeys();

            localizeDropdown.SetKey(localizationKeys);
        }
        else
        {
            // 後備方案：如果沒有 UILocalizeTMP_Dropdown 組件，使用傳統方法
            var data = networkSettingsUi.GetAllOptions();
            dropdown.ClearOptions();
            dropdown.AddOptions(data);
        }
    }

    /// <summary>
    /// 根據 maskType 查找對應的索引
    /// </summary>
    private int GetIndexOfMaskType(string maskType)
    {
        var maskIds = MaskTypeManager.Instance.GetMaskTypeIds();
        for (int i = 0; i < maskIds.Count; i++)
        {
            if (maskIds[i] == maskType)
            {
                return i;
            }
        }
        return 0;  // 如果找不到，返回第一個
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
                return "NotConnecting";
            }
        }
        return "Not Connecting";
    }

    #endregion

    #region 設定遮罩類型

    private void SetMaskType(int index)
    {
        // 從索引獲取對應的遮罩 ID
        var maskIds = MaskTypeManager.Instance.GetMaskTypeIds();

        if (index < 0 || index >= maskIds.Count)
        {
            Debug.LogWarning($"[Table] Index {index} is out of bounds for available mask types.");
            return;
        }

        maskType = maskIds[index];
        currentPortData.MaskType = maskType;
        HandleAction(OnMask);

        Debug.Log($"[Table] 設定遮罩類型: {maskType}");
    }

    #endregion

    #region 設定值

    private void SetValue(string uiKey, string content)
    {
        uiCollector.SetText(uiKey, content);
    }

    #endregion
}
