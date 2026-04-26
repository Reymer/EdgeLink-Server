using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using EdgeLink.Infrastructure;

namespace EdgeLink.WebApi;

public class AuthManager
{
    private static AuthManager? _instance;
    public static AuthManager Instance => _instance ??= new AuthManager();

    private string _passwordHash = "";
    private readonly ConcurrentDictionary<string, DateTime> _sessions = new();
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromHours(8);
    private readonly string _settingsPath;

    private AuthManager()
    {
        _settingsPath = Path.Combine(AppPaths.DataDir, "auth.json");
        Load();
    }

    private void Load()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var s = Json.FromJson<AuthSettings>(File.ReadAllText(_settingsPath));
                if (s != null && !string.IsNullOrEmpty(s.passwordHash))
                {
                    _passwordHash = s.passwordHash;
                    return;
                }
            }
            catch { }
        }
        _passwordHash = Hash("admin");
        Save();
        AppLogger.Log("[Auth] First run — default password: admin");
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, Json.ToJson(new AuthSettings { passwordHash = _passwordHash }));
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[Auth] Failed to save auth settings: {ex.Message}");
        }
    }

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
        if (expiry < DateTime.UtcNow) { _sessions.TryRemove(sid, out _); return false; }
        _sessions[sid] = DateTime.UtcNow + _sessionTimeout;
        return true;
    }

    public void DestroySession(string sid)
    {
        if (!string.IsNullOrEmpty(sid)) _sessions.TryRemove(sid, out _);
    }

    public bool IsAuthenticated(HttpListenerRequest request)
    {
        var cookie = request.Cookies["edgelink_sid"];
        return cookie != null && ValidateSession(cookie.Value);
    }

    private static string Hash(string input)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-", "").ToLowerInvariant();
    }

    private class AuthSettings { public string passwordHash = ""; }
}
