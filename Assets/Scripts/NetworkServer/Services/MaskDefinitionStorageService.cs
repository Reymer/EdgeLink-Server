using UnityEngine;

public class MaskDefinitionStorageService
{
    public MaskDefinitions Load()
    {
        try
        {
            return SettingLoader.Load<MaskDefinitions>() ?? new MaskDefinitions();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MaskDefinitionStorageService] 讀取失敗: {ex.Message}");
            return new MaskDefinitions();
        }
    }

    public void Save(MaskDefinitions data)
    {
        try
        {
            SettingLoader.Save(data);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MaskDefinitionStorageService] 儲存失敗: {ex.Message}");
        }
    }
}
