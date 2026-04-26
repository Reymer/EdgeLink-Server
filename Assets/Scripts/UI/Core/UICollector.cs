using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class UIAsset
{
    public string realKey;
    public string groupName;
    public string keyName;
    public UnityEngine.Object asset;
}

public class UICollector : MonoBehaviour
{
    public int            collectorID = -1;
    public string         guid        = string.Empty;
    public string         groupName;
    public List<UIAsset>  UIAsset     = new List<UIAsset>();

    private Dictionary<string, UIAsset>        _assetPair  = new();
    private Dictionary<string, List<Action>>   _onChecks   = new();
    private bool _initialized;

    private void Awake() => Init();

    public void Init()
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var item in UIAsset)
            if (!string.IsNullOrEmpty(item.realKey))
                _assetPair[item.realKey] = item;

        foreach (var item in UIAsset)
        {
            string key = item.realKey;
            if (item.asset is Button btn)
                btn.onClick.AddListener(() => FireCheck(key));
            if (item.asset is Toggle tog)
                tog.onValueChanged.AddListener(v => { if (v) FireCheck(key); });
        }
    }

    private void FireCheck(string key)
    {
        if (!_onChecks.TryGetValue(key, out var list)) return;
        foreach (var a in list.ToArray()) a?.Invoke();
    }

    public void BindOnCheck(string key, Action action)
    {
        if (!_onChecks.ContainsKey(key)) _onChecks[key] = new List<Action>();
        if (!_onChecks[key].Contains(action)) _onChecks[key].Add(action);
    }

    public T GetAsset<T>(string key) where T : UnityEngine.Object
    {
        if (!_assetPair.TryGetValue(key, out var item)) return null;
        if (item.asset is T direct) return direct;
        if (item.asset is GameObject go && go.TryGetComponent<T>(out var comp)) return comp;
        if (item.asset is Component c  && c.gameObject.TryGetComponent<T>(out var cc)) return cc;
        return null;
    }

    public void Active(string key)   => SetActive(key, true);
    public void Deactive(string key) => SetActive(key, false);
    public void SetActive(string key, bool active)
    {
        if (!_assetPair.TryGetValue(key, out var item)) return;
        var go = item.asset is GameObject g ? g :
                 item.asset is Component  c ? c.gameObject : null;
        go?.SetActive(active);
    }

    public void SetText(string key, string text)
    {
        if (!_assetPair.TryGetValue(key, out var item)) return;
        if (item.asset is TMP_Text   t) t.text = text;
        if (item.asset is InputField f) f.text = text;
    }
}
