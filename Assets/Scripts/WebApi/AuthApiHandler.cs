using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// /api/auth/login, /api/auth/logout, /api/auth/status
/// </summary>
public class AuthApiHandler
{
    /// <summary>POST /api/auth/login — body: {"password":"xxx"}</summary>
    public Task LoginAsync(HttpListenerContext ctx)
    {
        try
        {
            string body = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEnd();
            var req = JsonUtility.FromJson<LoginRequest>(body);

            if (req == null || !AuthManager.Instance.ValidatePassword(req.password))
            {
                HttpApiServer.WriteJson(ctx, 401, "{\"success\":false,\"error\":\"Invalid password\"}");
                return Task.CompletedTask;
            }

            string sid = AuthManager.Instance.CreateSession();
            ctx.Response.SetCookie(new Cookie("edgelink_sid", sid)
            {
                HttpOnly = true,
                Path = "/",
                Expires = DateTime.UtcNow.AddHours(8)
            });
            HttpApiServer.WriteJson(ctx, 200, "{\"success\":true}");
        }
        catch
        {
            HttpApiServer.WriteJson(ctx, 400, "{\"success\":false,\"error\":\"Bad request\"}");
        }
        return Task.CompletedTask;
    }

    /// <summary>POST /api/auth/logout</summary>
    public Task LogoutAsync(HttpListenerContext ctx)
    {
        var cookie = ctx.Request.Cookies["edgelink_sid"];
        if (cookie != null)
            AuthManager.Instance.DestroySession(cookie.Value);

        ctx.Response.SetCookie(new Cookie("edgelink_sid", "")
        {
            HttpOnly = true,
            Path = "/",
            Expires = DateTime.UtcNow.AddDays(-1)
        });
        HttpApiServer.WriteJson(ctx, 200, "{\"success\":true}");
        return Task.CompletedTask;
    }

    /// <summary>GET /api/auth/status</summary>
    public Task StatusAsync(HttpListenerContext ctx)
    {
        bool authed = AuthManager.Instance.IsAuthenticated(ctx.Request);
        HttpApiServer.WriteJson(ctx, 200, $"{{\"authenticated\":{(authed ? "true" : "false")}}}");
        return Task.CompletedTask;
    }

    /// <summary>POST /api/auth/change-password — 需認證；body: {"currentPassword":"...","newPassword":"..."}</summary>
    public Task ChangePasswordAsync(HttpListenerContext ctx)
    {
        try
        {
            string body = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEnd();
            var req = JsonUtility.FromJson<ChangePasswordRequest>(body);
            if (req == null || string.IsNullOrEmpty(req.currentPassword) || string.IsNullOrEmpty(req.newPassword))
            {
                HttpApiServer.WriteJson(ctx, 400, "{\"success\":false,\"error\":\"Bad request\"}");
                return Task.CompletedTask;
            }
            if (!AuthManager.Instance.ValidatePassword(req.currentPassword))
            {
                HttpApiServer.WriteJson(ctx, 401, "{\"success\":false,\"error\":\"Invalid current password\"}");
                return Task.CompletedTask;
            }
            AuthManager.Instance.ChangePassword(req.newPassword);
            HttpApiServer.WriteJson(ctx, 200, "{\"success\":true}");
        }
        catch
        {
            HttpApiServer.WriteJson(ctx, 400, "{\"success\":false,\"error\":\"Bad request\"}");
        }
        return Task.CompletedTask;
    }

    [Serializable]
    private class LoginRequest { public string password; }

    [Serializable]
    private class ChangePasswordRequest { public string currentPassword; public string newPassword; }
}
