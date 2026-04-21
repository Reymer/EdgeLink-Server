using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 遮罩定義管理器：管理所有遮罩的協定解析規則，持久化到 Setting/MaskDefinitions.setting
/// </summary>
public class MaskDefinitionManager
{
    private static MaskDefinitionManager instance;
    public static MaskDefinitionManager Instance => instance ??= new MaskDefinitionManager();

    private const string DEFAULT_MASK_ID = "OriginalData";

    private readonly MaskDefinitionStorageService storage = new();
    private readonly Dictionary<string, MaskDefinition> definitions = new();
    private readonly List<string> order = new();

    public event Action OnMaskTypesChanged;

    private MaskDefinitionManager()
    {
        Load();

        if (!definitions.ContainsKey(DEFAULT_MASK_ID))
        {
            AddDefinitionInternal(new MaskDefinition
            {
                maskId = DEFAULT_MASK_ID,
                localizationKey = DEFAULT_MASK_ID,
                description = "Forward raw data as-is",
                inputEncoding = "text",
                outputTemplate = "{raw}"
            });
            Save();
        }
    }

    // ── 讀寫 ──────────────────────────────────────────

    private void Load()
    {
        var data = storage.Load();
        foreach (var def in data.definitions)
        {
            if (string.IsNullOrEmpty(def.maskId)) continue;
            definitions[def.maskId] = def;
            order.Add(def.maskId);
        }
    }

    private void Save()
    {
        var data = new MaskDefinitions
        {
            definitions = order
                .Where(id => definitions.ContainsKey(id))
                .Select(id => definitions[id])
                .ToList()
        };
        storage.Save(data);
    }

    private void AddDefinitionInternal(MaskDefinition def)
    {
        definitions[def.maskId] = def;
        if (!order.Contains(def.maskId))
            order.Add(def.maskId);
    }

    // ── 遮罩 CRUD（供 Unity UI 及 Web API 使用）────────

    public void AddMaskType(string maskId, string localizationKey = null)
    {
        if (string.IsNullOrEmpty(maskId))
        {
            Debug.LogWarning("[MaskDefinitionManager] maskId cannot be empty");
            return;
        }
        if (definitions.ContainsKey(maskId))
        {
            Debug.LogWarning($"[MaskDefinitionManager] Mask '{maskId}' already exists");
            return;
        }

        AddDefinitionInternal(new MaskDefinition
        {
            maskId = maskId,
            localizationKey = string.IsNullOrEmpty(localizationKey) ? maskId : localizationKey,
            description = "",
            inputEncoding = "text",
            fieldDelimiter = ";",
            kvSeparator = ":",
            outputTemplate = "{raw}"
        });
        Save();
        OnMaskTypesChanged?.Invoke();
    }

    public void RemoveMaskType(string maskId)
    {
        if (maskId == DEFAULT_MASK_ID)
        {
            Debug.LogWarning($"[MaskDefinitionManager] Cannot delete default mask '{DEFAULT_MASK_ID}'");
            return;
        }
        if (!definitions.ContainsKey(maskId))
        {
            Debug.LogWarning($"[MaskDefinitionManager] Mask '{maskId}' not found");
            return;
        }

        definitions.Remove(maskId);
        order.Remove(maskId);
        Save();
        OnMaskTypesChanged?.Invoke();
    }

    public void RenameMask(string oldId, string newId)
    {
        if (oldId == DEFAULT_MASK_ID)
            throw new InvalidOperationException($"不能重命名預設遮罩 '{DEFAULT_MASK_ID}'");
        if (!definitions.ContainsKey(oldId))
            throw new KeyNotFoundException($"遮罩 '{oldId}' 不存在");
        if (string.IsNullOrWhiteSpace(newId))
            throw new ArgumentException("新名稱不能為空");
        if (definitions.ContainsKey(newId))
            throw new InvalidOperationException($"遮罩 '{newId}' 已存在");

        var def = definitions[oldId];
        def.maskId = newId;
        if (def.localizationKey == oldId) def.localizationKey = newId;

        definitions.Remove(oldId);
        int idx = order.IndexOf(oldId);
        if (idx >= 0) order[idx] = newId; else order.Add(newId);
        definitions[newId] = def;

        Save();
        OnMaskTypesChanged?.Invoke();
    }

    /// <summary>
    /// 更新或新增完整定義（供 Web API 使用）
    /// </summary>
    public void SaveDefinition(MaskDefinition def)
    {
        if (def == null || string.IsNullOrEmpty(def.maskId)) return;
        AddDefinitionInternal(def);
        Save();
        OnMaskTypesChanged?.Invoke();
    }

    // ── 查詢 ──────────────────────────────────────────

    public MaskDefinition GetDefinition(string maskId)
    {
        definitions.TryGetValue(maskId ?? "", out var def);
        return def;
    }

    public bool HasMaskType(string maskId) => definitions.ContainsKey(maskId ?? "");

    public List<string> GetMaskTypeIds() => new(order);

    public string GetLocalizationKey(string maskId) =>
        definitions.TryGetValue(maskId, out var def) ? def.localizationKey : maskId;

    public string[] GetLocalizationKeys() =>
        order.Select(id => definitions.TryGetValue(id, out var d) ? d.localizationKey : id).ToArray();

    public int GetCount() => definitions.Count;

    public void NotifyChanged() => OnMaskTypesChanged?.Invoke();
}
