using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class UIEntry
{
    public string key;
    public GameObject target;
}

/// <summary>
/// 替換 DevKit UICollector，將 UI key 對應到 GameObject，
/// 提供 GetAsset、SetActive、BindOnCheck、SetText 等操作。
/// </summary>
public class UICollector : MonoBehaviour
{
    [SerializeField] private List<UIEntry> entries = new();

    private Dictionary<string, GameObject> _map;

    private void Awake() => BuildMap();

    private void BuildMap()
    {
        _map = new Dictionary<string, GameObject>(entries.Count);
        foreach (var e in entries)
            if (!string.IsNullOrEmpty(e.key) && e.target != null)
                _map[e.key] = e.target;
    }

    private GameObject Get(string key)
    {
        if (_map == null) BuildMap();
        _map.TryGetValue(key, out var go);
        if (go == null) Debug.LogWarning($"[UICollector] Key not found: {key}");
        return go;
    }

    public T GetAsset<T>(string key) where T : class
    {
        var go = Get(key);
        if (go == null) return null;
        if (typeof(T) == typeof(GameObject)) return go as T;
        return go.GetComponent<T>();
    }

    public void SetActive(string key, bool status)
    {
        var go = Get(key);
        if (go != null) go.SetActive(status);
    }

    public void Active(string key)   => SetActive(key, true);
    public void Deactive(string key) => SetActive(key, false);

    public void SetText(string key, string content)
    {
        var go = Get(key);
        if (go == null) return;
        var tmp = go.GetComponent<TMP_Text>();
        if (tmp != null) { tmp.text = content; return; }
        var legacy = go.GetComponent<Text>();
        if (legacy != null) legacy.text = content;
    }

    /// <summary>
    /// 綁定 Button.onClick 或 Toggle.onValueChanged（值為 true 時觸發）。
    /// </summary>
    public void BindOnCheck(string key, Action callback)
    {
        var go = Get(key);
        if (go == null) return;

        var btn = go.GetComponent<Button>();
        if (btn != null) { btn.onClick.AddListener(() => callback()); return; }

        var toggle = go.GetComponent<Toggle>();
        if (toggle != null) { toggle.onValueChanged.AddListener(v => { if (v) callback(); }); return; }

        Debug.LogWarning($"[UICollector] BindOnCheck: no Button/Toggle on key '{key}'");
    }
}
