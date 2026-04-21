using System;
using System.Collections.Generic;

/// <summary>
/// Unity UI 相容層：薄包裝，委派給 MaskDefinitionManager
/// </summary>
public class MaskTypeManager
{
    private static MaskTypeManager instance;
    public static MaskTypeManager Instance => instance ??= new MaskTypeManager();

    public event Action OnMaskTypesChanged
    {
        add    => MaskDefinitionManager.Instance.OnMaskTypesChanged += value;
        remove => MaskDefinitionManager.Instance.OnMaskTypesChanged -= value;
    }

    private MaskTypeManager() { }

    public void AddMaskType(string maskId, string localizationKey = null)
        => MaskDefinitionManager.Instance.AddMaskType(maskId, localizationKey);

    public void RemoveMaskType(string maskId)
        => MaskDefinitionManager.Instance.RemoveMaskType(maskId);

    public List<string> GetMaskTypeIds()
        => MaskDefinitionManager.Instance.GetMaskTypeIds();

    public string[] GetLocalizationKeys()
        => MaskDefinitionManager.Instance.GetLocalizationKeys();

    public string GetLocalizationKey(string maskId)
        => MaskDefinitionManager.Instance.GetLocalizationKey(maskId);

    public bool HasMaskType(string maskId)
        => MaskDefinitionManager.Instance.HasMaskType(maskId);

    public int GetCount()
        => MaskDefinitionManager.Instance.GetCount();

    public void NotifyChanged()
        => MaskDefinitionManager.Instance.NotifyChanged();
}
