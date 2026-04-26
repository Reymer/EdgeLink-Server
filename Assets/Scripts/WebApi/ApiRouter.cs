using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class ApiRouter
{
    private readonly PortApiHandler _portHandler = new();
    private readonly MaskApiHandler _maskHandler = new();
    private readonly LogApiHandler _logHandler = new();
    private readonly MonitorApiHandler _monitorHandler = new();
    private readonly MonitorSseHandler _monitorSseHandler = new();
    private readonly LanguageApiHandler _languageHandler = new();
    private readonly AuthApiHandler _authHandler = new();
    private readonly SettingsApiHandler _settingsHandler = new();
    private readonly string _webUiPath;

    public ApiRouter(string webUiPath)
    {
        _webUiPath = webUiPath;
    }

    public async Task RouteAsync(HttpListenerContext ctx)
    {
        string method = ctx.Request.HttpMethod.ToUpperInvariant();
        string[] segments = ctx.Request.Url.AbsolutePath.Trim('/').Split('/');
        // segments[0] = "" or "api", segments[1] = "ports"/"masks", segments[2] = {id}, segments[3] = "mask"

        // GET / — 永遠提供 HTML（登入狀態由前端 JS 判斷）
        if (method == "GET" && ctx.Request.Url.AbsolutePath == "/")
        {
            await ServeUiAsync(ctx);
            return;
        }

        // GET /docs  — Swagger UI
        if (method == "GET" && ctx.Request.Url.AbsolutePath == "/docs")
        {
            await ServeStaticAsync(ctx, "docs.html", "text/html; charset=utf-8");
            return;
        }

        // GET /openapi.json
        if (method == "GET" && ctx.Request.Url.AbsolutePath == "/openapi.json")
        {
            await ServeStaticAsync(ctx, "openapi.json", "application/json; charset=utf-8");
            return;
        }

        // /api/auth/* — login / logout / status 不需認證；其他管理端點需認證
        if (segments.Length >= 2 && segments[0] == "api" && segments[1] == "auth")
        {
            if (method == "POST" && segments.Length == 3 && segments[2] == "login")  { await _authHandler.LoginAsync(ctx);  return; }
            if (method == "POST" && segments.Length == 3 && segments[2] == "logout") { await _authHandler.LogoutAsync(ctx); return; }
            if (method == "GET"  && segments.Length == 3 && segments[2] == "status") { await _authHandler.StatusAsync(ctx); return; }

            if (!AuthManager.Instance.IsAuthenticated(ctx.Request))
            {
                HttpApiServer.WriteJson(ctx, 401, "{\"success\":false,\"error\":\"Unauthorized\"}");
                return;
            }
            if (method == "POST" && segments.Length == 3 && segments[2] == "change-password") { await _authHandler.ChangePasswordAsync(ctx); return; }
        }

        // 其餘所有 /api/* 端點皆需認證
        if (segments.Length >= 1 && segments[0] == "api")
        {
            if (!AuthManager.Instance.IsAuthenticated(ctx.Request))
            {
                HttpApiServer.WriteJson(ctx, 401, "{\"success\":false,\"error\":\"Unauthorized\"}");
                return;
            }
        }

        // /api/ports
        if (segments.Length >= 2 && segments[0] == "api" && segments[1] == "ports")
        {
            if (method == "GET"    && segments.Length == 2) { await _portHandler.GetAllAsync(ctx); return; }
            if (method == "POST"   && segments.Length == 2) { await _portHandler.AddAsync(ctx);    return; }
            if (method == "DELETE" && segments.Length == 2) { await _portHandler.DeleteAsync(ctx); return; }
            // PUT /api/ports/{id}
            if (method == "PUT" && segments.Length == 3)
            {
                string portId = Uri.UnescapeDataString(segments[2]);
                await _portHandler.UpdateAsync(ctx, portId);
                return;
            }
            // POST /api/ports/{id}/mask
            if (method == "POST" && segments.Length == 4 && segments[3] == "mask")
            {
                string id = Uri.UnescapeDataString(segments[2]);
                await _portHandler.ChangeMaskAsync(ctx, id);
                return;
            }
            // POST /api/ports/{id}/enabled
            if (method == "POST" && segments.Length == 4 && segments[3] == "enabled")
            {
                string id = Uri.UnescapeDataString(segments[2]);
                await _portHandler.ToggleEnabledAsync(ctx, id);
                return;
            }
            // GET /api/ports/{id}/clients
            if (method == "GET" && segments.Length == 4 && segments[3] == "clients")
            {
                string id = Uri.UnescapeDataString(segments[2]);
                await _portHandler.GetClientsAsync(ctx, id);
                return;
            }
        }

        // /api/masks
        if (segments.Length >= 2 && segments[0] == "api" && segments[1] == "masks")
        {
            if (method == "GET" && segments.Length == 2)
            {
                await _maskHandler.GetAllAsync(ctx);
                return;
            }
            if (method == "POST" && segments.Length == 2)
            {
                await _maskHandler.AddAsync(ctx);
                return;
            }
            // POST /api/masks/{id}/rename
            if (method == "POST" && segments.Length == 4 && segments[3] == "rename")
            {
                string maskId = Uri.UnescapeDataString(segments[2]);
                await _maskHandler.RenameAsync(ctx, maskId);
                return;
            }

            if (segments.Length == 3)
            {
                string maskId = Uri.UnescapeDataString(segments[2]);
                if (method == "GET")
                {
                    await _maskHandler.GetDefinitionAsync(ctx, maskId);
                    return;
                }
                if (method == "PUT")
                {
                    await _maskHandler.SaveDefinitionAsync(ctx, maskId);
                    return;
                }
                if (method == "DELETE")
                {
                    await _maskHandler.DeleteAsync(ctx, maskId);
                    return;
                }
            }
        }

        // GET /api/logs
        if (method == "GET" && segments.Length == 2 && segments[0] == "api" && segments[1] == "logs")
        {
            await _logHandler.GetLogsAsync(ctx);
            return;
        }

        // GET /api/monitor-logs
        if (method == "GET" && segments.Length == 2 && segments[0] == "api" && segments[1] == "monitor-logs")
        {
            await _logHandler.GetMonitorLogsAsync(ctx);
            return;
        }

        // GET /api/monitor-stream  (SSE)
        if (method == "GET" && segments.Length == 2 && segments[0] == "api" && segments[1] == "monitor-stream")
        {
            await _monitorSseHandler.HandleAsync(ctx);
            return;
        }

        // /api/monitor/port
        if (segments.Length >= 3 && segments[0] == "api" && segments[1] == "monitor" && segments[2] == "port")
        {
            if (method == "POST")   { await _monitorHandler.SetMonitorPortAsync(ctx);   return; }
            if (method == "DELETE") { await _monitorHandler.ClearMonitorPortAsync(ctx); return; }
            if (method == "GET")    { await _monitorHandler.GetMonitorPortAsync(ctx);   return; }
        }

        // /api/language
        if (segments.Length == 2 && segments[0] == "api" && segments[1] == "language")
        {
            if (method == "GET")  { await _languageHandler.GetAsync(ctx); return; }
            if (method == "POST") { await _languageHandler.SetAsync(ctx); return; }
        }

        // /api/settings
        if (segments.Length == 3 && segments[0] == "api" && segments[1] == "settings")
        {
            if (method == "GET"  && segments[2] == "export") { await _settingsHandler.ExportAsync(ctx); return; }
            if (method == "POST" && segments[2] == "import") { await _settingsHandler.ImportAsync(ctx); return; }
        }

        HttpApiServer.WriteError(ctx, 404, $"Not found: {method} {ctx.Request.Url.AbsolutePath}");
    }

    private async Task ServeUiAsync(HttpListenerContext ctx)
    {
        try
        {
            string html = await Task.Run(() => File.ReadAllText(_webUiPath, Encoding.UTF8));
            byte[] buf = Encoding.UTF8.GetBytes(html);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.Close();
        }
        catch (FileNotFoundException)
        {
            HttpApiServer.WriteError(ctx, 404, $"index.html not found at: {_webUiPath}");
        }
    }

    private async Task ServeStaticAsync(HttpListenerContext ctx, string fileName, string contentType)
    {
        string dir = Path.GetDirectoryName(_webUiPath) ?? ".";
        string filePath = Path.Combine(dir, fileName);
        try
        {
            byte[] buf = await Task.Run(() => File.ReadAllBytes(filePath));
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.Close();
        }
        catch (FileNotFoundException)
        {
            HttpApiServer.WriteError(ctx, 404, $"{fileName} not found");
        }
    }
}
