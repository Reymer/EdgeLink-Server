using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

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
                outputTemplate = "{raw}"
            });
            Save();
        }
    }

    // ── 讀寫 ──────────────────────────────────────────

    private void Load()
    {
        var data = storage.Load();
        _lock.EnterWriteLock();
        try
        {
            foreach (var def in data.definitions)
            {
                if (string.IsNullOrEmpty(def.maskId)) continue;
                definitions[def.maskId] = def;
                order.Add(def.maskId);
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    private void Save()
    {
        MaskDefinitions data;
        _lock.EnterReadLock();
        try
        {
            data = new MaskDefinitions
            {
                definitions = order
                    .Where(id => definitions.ContainsKey(id))
                    .Select(id => definitions[id])
                    .ToList()
            };
        }
        finally { _lock.ExitReadLock(); }
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

        _lock.EnterWriteLock();
        try
        {
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
                fieldDelimiter = ";",
                kvSeparator = ":",
                outputTemplate = "{raw}"
            });
        }
        finally { _lock.ExitWriteLock(); }
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

        _lock.EnterWriteLock();
        try
        {
            if (!definitions.ContainsKey(maskId))
            {
                Debug.LogWarning($"[MaskDefinitionManager] Mask '{maskId}' not found");
                return;
            }
            definitions.Remove(maskId);
            order.Remove(maskId);
        }
        finally { _lock.ExitWriteLock(); }
        Save();
        OnMaskTypesChanged?.Invoke();
    }

    public void RenameMask(string oldId, string newId)
    {
        if (oldId == DEFAULT_MASK_ID)
            throw new InvalidOperationException($"不能重命名預設遮罩 '{DEFAULT_MASK_ID}'");
        if (string.IsNullOrWhiteSpace(newId))
            throw new ArgumentException("新名稱不能為空");

        _lock.EnterWriteLock();
        try
        {
            if (!definitions.ContainsKey(oldId))
                throw new KeyNotFoundException($"遮罩 '{oldId}' 不存在");
            if (definitions.ContainsKey(newId))
                throw new InvalidOperationException($"遮罩 '{newId}' 已存在");

            var def = definitions[oldId];
            def.maskId = newId;
            if (def.localizationKey == oldId) def.localizationKey = newId;
            definitions.Remove(oldId);
            int idx = order.IndexOf(oldId);
            if (idx >= 0) order[idx] = newId; else order.Add(newId);
            definitions[newId] = def;
        }
        finally { _lock.ExitWriteLock(); }
        Save();
        OnMaskTypesChanged?.Invoke();
    }

    /// <summary>
    /// 更新或新增完整定義（供 Web API 使用）
    /// </summary>
    public void SaveDefinition(MaskDefinition def)
    {
        if (def == null || string.IsNullOrEmpty(def.maskId)) return;
        _lock.EnterWriteLock();
        try { AddDefinitionInternal(def); }
        finally { _lock.ExitWriteLock(); }
        Save();
        OnMaskTypesChanged?.Invoke();
    }

    // ── 查詢 ──────────────────────────────────────────

    public MaskDefinition GetDefinition(string maskId)
    {
        _lock.EnterReadLock();
        try { definitions.TryGetValue(maskId ?? "", out var def); return def; }
        finally { _lock.ExitReadLock(); }
    }

    public bool HasMaskType(string maskId)
    {
        _lock.EnterReadLock();
        try { return definitions.ContainsKey(maskId ?? ""); }
        finally { _lock.ExitReadLock(); }
    }

    public List<string> GetMaskTypeIds()
    {
        _lock.EnterReadLock();
        try { return new List<string>(order); }
        finally { _lock.ExitReadLock(); }
    }

    public string GetLocalizationKey(string maskId)
    {
        _lock.EnterReadLock();
        try { return definitions.TryGetValue(maskId, out var def) ? def.localizationKey : maskId; }
        finally { _lock.ExitReadLock(); }
    }

    public string[] GetLocalizationKeys()
    {
        _lock.EnterReadLock();
        try { return order.Select(id => definitions.TryGetValue(id, out var d) ? d.localizationKey : id).ToArray(); }
        finally { _lock.ExitReadLock(); }
    }

    public int GetCount()
    {
        _lock.EnterReadLock();
        try { return definitions.Count; }
        finally { _lock.ExitReadLock(); }
    }

    public void NotifyChanged() => OnMaskTypesChanged?.Invoke();
}
