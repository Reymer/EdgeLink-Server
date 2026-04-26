using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 管理 Web API 認證：session cookie
/// </summary>
public class AuthManager
{
    private static AuthManager _instance;
    public static AuthManager Instance => _instance ??= new AuthManager();

    private string _passwordHash;

    private readonly ConcurrentDictionary<string, DateTime> _sessions = new();
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromHours(8);

    private string _settingsPath;

    private AuthManager()
    {
        _settingsPath = Path.Combine(Application.persistentDataPath, "auth.json");
        Load();
    }

    // ── 設定讀寫 ────────────────────────────────────────────────────────────

    private void Load()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var s = JsonUtility.FromJson<AuthSettings>(File.ReadAllText(_settingsPath));
                if (!string.IsNullOrEmpty(s.passwordHash))
                {
                    _passwordHash = s.passwordHash;
                    return;
                }
            }
            catch { }
        }

        // 首次啟動：預設密碼 admin
        _passwordHash = Hash("admin");
        Save();
        Debug.Log("[Auth] First run — default password: admin");
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_settingsPath,
                JsonUtility.ToJson(new AuthSettings { passwordHash = _passwordHash }));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Auth] Failed to save auth settings: {ex.Message}");
        }
    }

    // ── 公開介面 ────────────────────────────────────────────────────────────

    public void ChangePassword(string newPassword)
    {
        _passwordHash = Hash(newPassword);
        Save();
    }

    public bool ValidatePassword(string password) =>
        !string.IsNullOrEmpty(password) && Hash(password) == _passwordHash;

    public string CreateSession()
    {
        string sid = Guid.NewGuid().ToString("N");
        _sessions[sid] = DateTime.UtcNow + _sessionTimeout;
        return sid;
    }

    public bool ValidateSession(string sid)
    {
        if (string.IsNullOrEmpty(sid)) return false;
        if (!_sessions.TryGetValue(sid, out var expiry)) return false;
        if (expiry < DateTime.UtcNow)
        {
            _sessions.TryRemove(sid, out _);
            return false;
        }
        _sessions[sid] = DateTime.UtcNow + _sessionTimeout; // sliding window
        return true;
    }

    public void DestroySession(string sid)
    {
        if (!string.IsNullOrEmpty(sid))
            _sessions.TryRemove(sid, out _);
    }

    /// <summary>
    /// 主要認證入口：先查 API key header，再查 session cookie
    /// </summary>
    public bool IsAuthenticated(HttpListenerRequest request)
    {
        var cookie = request.Cookies["edgelink_sid"];
        return cookie != null && ValidateSession(cookie.Value);
    }

    // ── 工具 ────────────────────────────────────────────────────────────────

    private static string Hash(string input)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-", "").ToLowerInvariant();
    }

    [Serializable]
    private class AuthSettings
    {
        public string passwordHash;
    }
}
