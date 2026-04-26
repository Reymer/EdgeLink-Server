using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 掛在 TMP_Dropdown 物件上，依語言 key 陣列自動更新選項文字。
/// 呼叫 SetKey(keys) 設定每個選項對應的本地化 key。
/// </summary>
[RequireComponent(typeof(TMP_Dropdown))]
public class UILocalizeTMP_Dropdown : MonoBehaviour
{
    private TMP_Dropdown _dropdown;
    private string[]     _keys;
    private bool         _initialized;

    public void Init()
    {
        if (_initialized) return;
        _initialized = true;
        _dropdown = GetComponent<TMP_Dropdown>();
        Localization.Instance.LanguageChanged += Refresh;
    }

    private void OnDestroy()
    {
        Localization.Instance.LanguageChanged -= Refresh;
    }

    /// <summary>設定每個選項對應的本地化 key，並立即刷新顯示。</summary>
    public void SetKey(string[] keys)
    {
        if (!_initialized) Init();
        _keys = keys;
        Refresh();
    }

    private void Refresh()
    {
        if (_dropdown == null || _keys == null) return;

        int savedIndex = _dropdown.value;

        var options = new List<TMP_Dropdown.OptionData>(_keys.Length);
        foreach (var k in _keys)
            options.Add(new TMP_Dropdown.OptionData(Localization.Instance.GetText(k)));

        _dropdown.ClearOptions();
        _dropdown.AddOptions(options);

        // 還原選擇
        _dropdown.SetValueWithoutNotify(
            Mathf.Clamp(savedIndex, 0, options.Count - 1));
        _dropdown.RefreshShownValue();
    }
}
