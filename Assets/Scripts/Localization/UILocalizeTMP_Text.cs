using TMPro;
using UnityEngine;

/// <summary>
/// 掛在 TMP_Text 物件上，依語言 key 自動更新文字。
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class UILocalizeTMP_Text : MonoBehaviour
{
    [SerializeField] private string key;

    private TMP_Text _label;

    private void Awake()
    {
        _label = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        Localization.Instance.LanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        Localization.Instance.LanguageChanged -= Refresh;
    }

    public void SetKey(string newKey)
    {
        key = newKey;
        Refresh();
    }

    private void Refresh()
    {
        if (_label == null) _label = GetComponent<TMP_Text>();
        if (!string.IsNullOrEmpty(key))
            _label.text = Localization.Instance.GetText(key);
    }
}
