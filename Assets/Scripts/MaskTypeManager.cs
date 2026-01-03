using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 簡單的遮罩類型管理器
/// 只負責管理遮罩類型列表，支持新增、刪除和本地化
/// </summary>
public class MaskTypeManager
{
    private static MaskTypeManager instance;
    public static MaskTypeManager Instance => instance ??= new MaskTypeManager();

    // 遮罩類型列表（ID -> 本地化鍵）
    private readonly Dictionary<string, string> maskTypes = new();
    private readonly List<string> maskTypeOrder = new();  // 保持順序

    /// <summary>
    /// 遮罩列表變化事件
    /// </summary>
    public event Action OnMaskTypesChanged;

    private MaskTypeManager()
    {
        // 添加預設遮罩
        AddMaskType("OriginalData");
    }

    /// <summary>
    /// 新增遮罩類型
    /// </summary>
    /// <param name="maskId">遮罩 ID（內部使用）</param>
    /// <param name="localizationKey">本地化鍵（用於顯示，可選）</param>
    public void AddMaskType(string maskId, string localizationKey = null)
    {
        if (string.IsNullOrEmpty(maskId))
        {
            Debug.LogWarning("[MaskTypeManager] 遮罩 ID 不能為空");
            return;
        }

        if (maskTypes.ContainsKey(maskId))
        {
            Debug.LogWarning($"[MaskTypeManager] 遮罩 {maskId} 已存在");
            return;
        }

        // 如果沒有提供本地化鍵，使用 ID 本身
        string key = string.IsNullOrEmpty(localizationKey) ? maskId : localizationKey;

        maskTypes[maskId] = key;
        maskTypeOrder.Add(maskId);

        Debug.Log($"[MaskTypeManager] 已新增遮罩: {maskId} (本地化鍵: {key})");

        // 觸發變化事件
        OnMaskTypesChanged?.Invoke();
    }

    /// <summary>
    /// 刪除遮罩類型
    /// </summary>
    public void RemoveMaskType(string maskId)
    {
        if (maskId == "original_data")
        {
            Debug.LogWarning("[MaskTypeManager] 不能刪除預設遮罩 'original_data'");
            return;
        }

        if (!maskTypes.ContainsKey(maskId))
        {
            Debug.LogWarning($"[MaskTypeManager] 遮罩 {maskId} 不存在");
            return;
        }

        maskTypes.Remove(maskId);
        maskTypeOrder.Remove(maskId);

        Debug.Log($"[MaskTypeManager] 已刪除遮罩: {maskId}");

        // 觸發變化事件
        OnMaskTypesChanged?.Invoke();
    }

    /// <summary>
    /// 獲取所有遮罩 ID（按順序）
    /// </summary>
    public List<string> GetMaskTypeIds()
    {
        return new List<string>(maskTypeOrder);
    }

    /// <summary>
    /// 獲取所有本地化鍵（用於 UILocalizeTMP_Dropdown）
    /// </summary>
    public string[] GetLocalizationKeys()
    {
        var keys = new string[maskTypeOrder.Count];
        for (int i = 0; i < maskTypeOrder.Count; i++)
        {
            keys[i] = maskTypes[maskTypeOrder[i]];
        }
        return keys;
    }

    /// <summary>
    /// 獲取遮罩的本地化鍵
    /// </summary>
    public string GetLocalizationKey(string maskId)
    {
        return maskTypes.ContainsKey(maskId) ? maskTypes[maskId] : maskId;
    }

    /// <summary>
    /// 檢查遮罩是否存在
    /// </summary>
    public bool HasMaskType(string maskId)
    {
        return maskTypes.ContainsKey(maskId);
    }

    /// <summary>
    /// 獲取遮罩數量
    /// </summary>
    public int GetCount()
    {
        return maskTypes.Count;
    }

    /// <summary>
    /// 手動觸發變化事件
    /// </summary>
    public void NotifyChanged()
    {
        OnMaskTypesChanged?.Invoke();
    }
}
