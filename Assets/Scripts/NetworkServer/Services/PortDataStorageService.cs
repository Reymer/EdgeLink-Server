using System.Collections.Generic;
using UnityEngine;
using DevKit;

public class PortDataStorageService
{
    /// <summary>
    /// 讀取 PortDatas 容器，若無則回傳空容器
    /// </summary>
    public PortDatas LoadPortDatas()
    {
        try
        {
            var portDatas = SettingLoader.Load<PortDatas>();
            if (portDatas == null)
            {
                Debug.LogWarning("PortDatas 為 null，回傳空容器。");
                return new PortDatas();  // 若讀取失敗，回傳空容器
            }
            return portDatas;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"讀取 PortDatas 時發生錯誤: {ex.Message}");
            return new PortDatas();  // 發生錯誤時，回傳空容器
        }
    }

    /// <summary>
    /// 讀取 PortData 列表（內含於 PortDatas）
    /// </summary>
    public List<PortData> LoadPortData()
    {
        return LoadPortDatas().portDatas;
    }

    /// <summary>
    /// 儲存 PortDatas 容器
    /// </summary>
    public void SavePortDatas(PortDatas container)
    {
        try
        {
            SettingLoader.Save(container);
            Debug.Log($"已透過 SettingLoader 儲存 PortDatas，共 {container.portDatas.Count} 筆資料");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"儲存 PortDatas 時發生錯誤: {ex.Message}");
        }
    }

    /// <summary>
    /// 儲存 PortData 列表（包裝進 PortDatas）
    /// </summary>
    public void SavePortData(List<PortData> data)
    {
        var container = new PortDatas { portDatas = data };
        SavePortDatas(container);
    }

    /// <summary>
    /// 讀取 TcpClientRetryConfig，若無則回傳預設
    /// </summary>
    public TcpClientRetryConfig LoadRetryConfig()
    {
        try
        {
            return SettingLoader.Load<TcpClientRetryConfig>() ?? new TcpClientRetryConfig();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"讀取 TcpClientRetryConfig 時發生錯誤: {ex.Message}");
            return new TcpClientRetryConfig();
        }
    }

    /// <summary>
    /// 儲存 TcpClientRetryConfig
    /// </summary>
    public void SaveRetryConfig(TcpClientRetryConfig config)
    {
        try
        {
            SettingLoader.Save(config);
            Debug.Log("已透過 SettingLoader 儲存 TcpClientRetryConfig");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"儲存 TcpClientRetryConfig 時發生錯誤: {ex.Message}");
        }
    }
}
