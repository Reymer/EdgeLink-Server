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
    public static Localization Instance => _instance ??= new Localization();

    // ── 事件 ──────────────────────────────────────────────────────────────
    public event Action LanguageChanged;

    // ── 內部資料 ──────────────────────────────────────────────────────────
    private readonly List<string> _langCodes   = new();   // ["zh-TW","en-US","ja-JP"]
    private readonly List<string> _langNames   = new();   // ["繁體中文","English","日本語"]
    private readonly Dictionary<string, string[]> _table = new(); // key → values per lang

    private int _currentIndex;

    private const string FileName    = "LanguageTable.dat";
    private const char   ColSep      = '|';
    private const char   RowEnd      = '┤';
    private const string PrefKey     = "Localization_LangIndex";

    // ── 建構 ──────────────────────────────────────────────────────────────
    private Localization()
    {
        Load();
        _currentIndex = PlayerPrefs.GetInt(PrefKey, 0);
    }

    private void Load()
    {
        string path = Path.Combine(
            Application.dataPath, "..", "GameData", FileName);

        if (!File.Exists(path))
        {
            Debug.LogError($"[Localization] 找不到語言表：{path}");
            return;
        }

        string raw = File.ReadAllText(path, Encoding.UTF8);
        // 去 BOM、換行統一
        raw = raw.TrimStart('﻿').Replace("\r\n", "\n").Replace("\r", "\n");

        // 以 ┤ 切割列（過濾空列）
        string[] rows = raw.Split(RowEnd, StringSplitOptions.RemoveEmptyEntries);

        bool isHeader = true;
        int langCount = 0;

        foreach (string row in rows)
        {
            string line = row.Trim('\n', ' ');
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] cols = line.Split(ColSep);

            if (isHeader)
            {
                // 第一欄是 "Key"，其餘是 "langCode,顯示名稱"
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
                values[i] = (i + 1 < cols.Length) ? cols[i + 1] : string.Empty;

            _table[key] = values;
        }
    }

    // ── 公開 API ──────────────────────────────────────────────────────────

    /// <summary>取得目前語言的文字，找不到 key 時回傳 key 本身。</summary>
    public string GetText(string key)
    {
        if (_table.TryGetValue(key, out var values) && _currentIndex < values.Length)
            return values[_currentIndex];
        return key;
    }

    /// <summary>切換語言（依索引）。</summary>
    public void SetCurrentLanguage(int index)
    {
        if (index < 0 || index >= _langCodes.Count) return;
        _currentIndex = index;
        PlayerPrefs.SetInt(PrefKey, index);
        PlayerPrefs.Save();
        LanguageChanged?.Invoke();
    }

    /// <summary>取得目前語言索引。</summary>
    public int GetCurrentLanguageIndex() => _currentIndex;

    /// <summary>取得所有語言的顯示名稱陣列（供 Dropdown 用）。</summary>
    public string[] GetAllLanguageShownNames() => _langNames.ToArray();

    /// <summary>取得目前語言代碼（例如 "zh-TW"）。</summary>
    public string GetCurrentLanguageCode() =>
        _currentIndex < _langCodes.Count ? _langCodes[_currentIndex] : string.Empty;
}
