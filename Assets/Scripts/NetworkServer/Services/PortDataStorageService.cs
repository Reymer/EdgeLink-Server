using System.Collections.Generic;
using System;
using UnityEngine;

public class PortDataStorageService
{
    private readonly string filePath;

    public PortDataStorageService(string path)
    {
        filePath = path;
    }

    public List<PortData> Load()
    {
        try
        {
            return JsonFileHandler.LoadFromJson<PortData>(filePath) ?? new();
        }
        catch (Exception ex)
        {
            Debug.LogError("無法載入資料: " + ex);
            return new();
        }
    }

    public void Save(List<PortData> data)
    {
        try
        {
            JsonFileHandler.SaveToJson(filePath, data);
        }
        catch (Exception ex)
        {
            Debug.LogError("無法保存端口資料: " + ex);
        }
    }
}