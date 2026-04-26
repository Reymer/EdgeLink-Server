using System.Net;
using System.Text;
using EdgeLink.Infrastructure;

namespace EdgeLink.WebApi;

public class AuthApiHandler
{
    public Task LoginAsync(HttpListenerContext ctx)
    {
        try
        {
            string body = new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEnd();
            var req = Json.FromJson<LoginRequest>(body);
            if (req == null || !AuthManager.Instance.ValidatePassword(req.password ?? ""))
            {
                HttpApiServer.WriteJson(ctx, 401, "{\"success\":false,\"error\":\"Invalid password\"}");
                return Task.CompletedTask;
            }
            string sid = AuthManager.Instance.CreateSession();
            ctx.Response.SetCookie(new Cookie("edgelink_sid", sid)
            {
                HttpOnly = true, Path = "/",
                Expires = DateTime.UtcNow.AddHours(8)
            });
            HttpApiServer.WriteJson(ctx, 200, "{\"success\":true}");
        }
        catch { HttpApiServer.WriteJson(ctx, 400, "{\"success\":false,\"error\":\"Bad request\"}"); }
        return Task.CompletedTask;
    }

    public Task LogoutAsync(HttpListenerContext ctx)
    {
        var cookie = ctx.Request.Cookies["edgelink_sid"];
        if (cookie != null) AuthManager.Instance.DestroySession(cookie.Value);
        ctx.Response.SetCookie(new Cookie("edgelink_sid", "")
        {
            HttpOnly = true, Path = "/",
            Expires = DateTime.UtcNow.AddDays(-1)
        });
        HttpApiServer.WriteJson(ctx, 200, "{\"success\":true}");
        return Task.CompletedTask;
    }

    public Task StatusAsync(HttpListenerContext ctx)
    {
        bool authed = AuthManager.Instance.IsAuthenticated(ctx.Request);
        HttpApiServer.WriteJson(ctx, 200, $"{{\"authenticated\":{(authed ? "true" : "false")}}}");
        return Task.CompletedTask;
    }

    public Task ChangePasswordAsync(HttpListenerContext ctx)
    {
        try
        {
            string body = new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEnd();
            var req = Json.FromJson<ChangePasswordRequest>(body);
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
        catch { HttpApiServer.WriteJson(ctx, 400, "{\"success\":false,\"error\":\"Bad request\"}"); }
        return Task.CompletedTask;
    }

    private class LoginRequest          { public string password = ""; }
    private class ChangePasswordRequest { public string currentPassword = ""; public string newPassword = ""; }
}
