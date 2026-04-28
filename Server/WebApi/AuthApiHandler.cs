using System.Net;
using System.Text;
using System.Text.Json;

namespace EdgeLink.WebApi;

public class AuthApiHandler
{
    public Task LoginAsync(HttpListenerContext ctx)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8, leaveOpen: true);
            string body = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(body);
            string password = doc.RootElement.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";

            if (!AuthManager.Instance.ValidatePassword(password))
            {
                HttpApiServer.WriteJson(ctx, 401, "{\"success\":false,\"error\":\"Invalid password\"}");
                return Task.CompletedTask;
            }
            string sid = AuthManager.Instance.CreateSession();
            SetSessionCookie(ctx, sid, DateTime.UtcNow.AddHours(8));
            HttpApiServer.WriteJson(ctx, 200, "{\"success\":true}");
        }
        catch { HttpApiServer.WriteJson(ctx, 400, "{\"success\":false,\"error\":\"Bad request\"}"); }
        return Task.CompletedTask;
    }

    public Task LogoutAsync(HttpListenerContext ctx)
    {
        var cookie = ctx.Request.Cookies["edgelink_sid"];
        if (cookie != null) AuthManager.Instance.DestroySession(cookie.Value);
        SetSessionCookie(ctx, "", DateTime.UtcNow.AddDays(-1));
        HttpApiServer.WriteJson(ctx, 200, "{\"success\":true}");
        return Task.CompletedTask;
    }

    public Task StatusAsync(HttpListenerContext ctx)
    {
        bool authed = AuthManager.Instance.IsAuthenticated(ctx.Request);
        HttpApiServer.WriteJson(ctx, 200, $"{{\"authenticated\":{(authed ? "true" : "false")}}}");
        return Task.CompletedTask;
    }

    private static void SetSessionCookie(HttpListenerContext ctx, string value, DateTime expires)
    {
        string header = $"edgelink_sid={value}; HttpOnly; Path=/; SameSite=Strict; Expires={expires:R}";
        if (ctx.Request.IsSecureConnection)
            header += "; Secure";
        ctx.Response.Headers.Add("Set-Cookie", header);
    }

    public Task ChangePasswordAsync(HttpListenerContext ctx)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8, leaveOpen: true);
            string body = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(body);
            string current = doc.RootElement.TryGetProperty("currentPassword", out var c) ? c.GetString() ?? "" : "";
            string next    = doc.RootElement.TryGetProperty("newPassword",     out var n) ? n.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(next))
            {
                HttpApiServer.WriteJson(ctx, 400, "{\"success\":false,\"error\":\"Bad request\"}");
                return Task.CompletedTask;
            }
            if (!AuthManager.Instance.ValidatePassword(current))
            {
                HttpApiServer.WriteJson(ctx, 401, "{\"success\":false,\"error\":\"Invalid current password\"}");
                return Task.CompletedTask;
            }
            AuthManager.Instance.ChangePassword(next);
            HttpApiServer.WriteJson(ctx, 200, "{\"success\":true}");
        }
        catch { HttpApiServer.WriteJson(ctx, 400, "{\"success\":false,\"error\":\"Bad request\"}"); }
        return Task.CompletedTask;
    }
}
