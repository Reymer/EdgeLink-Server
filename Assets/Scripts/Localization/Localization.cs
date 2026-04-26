using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 多語系管理單例，讀取 GameData/LanguageTable.dat。
/// 格式：Key|langCode,顯示名稱|... 每列以 ┤ 結尾。
/// </summary>
public class Localization
{
    // ── Singleton ─────────────────────────────────────────────────────────
    private static Localization _instance;
    public static Localization Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new Localization();
                _instance.Load();
                _instance._currentIndex = PlayerPrefs.GetInt(PrefKey, 0);
            }
            return _instance;
        }
    }

    // ── 事件 ──────────────────────────────────────────────────────────────
    public event Action LanguageChanged;

    // ── 內部資料 ──────────────────────────────────────────────────────────
    private readonly List<string> _langCodes = new();
    private readonly List<string> _langNames = new();
    private readonly Dictionary<string, string[]> _table = new();

    private int _currentIndex;
    private bool _loaded;

    private const string FileName = "LanguageTable.dat";
    private const char   ColSep   = '|';
    private const char   RowEnd   = '┤';
    private const string PrefKey  = "SelectedLanguageIndex"; // 與 NetworkSettingsUI 共用同一個 key

    private Localization() { }

    // ── RuntimeInitialize：場景載入前在主執行緒初始化 ─────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init() => _ = Instance;

    // ── 載入語言表 ────────────────────────────────────────────────────────
    private void Load()
    {
        if (_loaded) return;
        _loaded = true;

        string path = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "GameData", FileName));

        if (!File.Exists(path))
        {
            Debug.LogError($"[Localization] 找不到語言表：{path}");
            return;
        }

        string raw = File.ReadAllText(path, Encoding.UTF8)
                         .TrimStart('﻿')           // 去 BOM
                         .Replace("\r\n", "\n")
                         .Replace("\r", "\n");

        string[] rows = raw.Split(RowEnd, StringSplitOptions.RemoveEmptyEntries);

        bool isHeader = true;
        int  langCount = 0;

        foreach (string row in rows)
        {
            string line = row.Trim('\n', ' ');
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] cols = line.Split(ColSep);

            if (isHeader)
            {
                for (int i = 1; i < cols.Length; i++)
                {
                    string[] parts = cols[i].Split(',');
                    _langCodes.Add(parts[0].Trim());
                    _langNames.Add(parts.Length > 1 ? parts[1].Trim() : parts[0].Trim());
                }
                langCount = _langCodes.Count;
                isHeader = false;
                continue;
            }

            if (cols.Length < 1) continue;
            string key = cols[0].Trim();
            var values = new string[langCount];
            for (int i = 0; i < langCount; i++)
                values[i] = (i + 1 < cols.Length) ? cols[i + 1].Trim() : string.Empty;

            _table[key] = values;
        }

        Debug.Log($"[Localization] 載入完成：{_table.Count} 個 key，{langCount} 種語言");
    }

    // ── 公開 API ──────────────────────────────────────────────────────────

    public string GetText(string key)
    {
        if (_table.TryGetValue(key, out var values) && _currentIndex < values.Length)
            return values[_currentIndex];
        return key;
    }

    public void SetCurrentLanguage(int index)
    {
        if (index < 0 || index >= _langCodes.Count) return;
        _currentIndex = index;
        PlayerPrefs.SetInt(PrefKey, index);
        PlayerPrefs.Save();
        LanguageChanged?.Invoke();
    }

    public int GetCurrentLanguageIndex() => _currentIndex;

    public string[] GetAllLanguageShownNames() => _langNames.ToArray();

    public string GetCurrentLanguageCode() =>
        _currentIndex < _langCodes.Count ? _langCodes[_currentIndex] : string.Empty;
}
