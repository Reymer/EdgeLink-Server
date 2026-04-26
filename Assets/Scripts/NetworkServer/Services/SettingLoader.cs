using System.IO;
using System.Text;
using UnityEngine;

public static class SettingLoader
{
    private static string SettingDir =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Setting"));

    private static string FilePath<T>() =>
        Path.Combine(SettingDir, typeof(T).Name + ".setting");

    public static T Load<T>() where T : new()
    {
        string path = FilePath<T>();
        if (!File.Exists(path)) return new T();
        string json = File.ReadAllText(path, Encoding.UTF8);
        return JsonUtility.FromJson<T>(json) ?? new T();
    }

    public static void Save<T>(T data)
    {
        Directory.CreateDirectory(SettingDir);
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(FilePath<T>(), json, Encoding.UTF8);
    }
}
