using System.Net;
using System.Text;
using EdgeLink.Infrastructure;

namespace EdgeLink.WebApi;

public class HttpApiServer
{
    private HttpListener? _listener;
    private Thread? _listenerThread;
    private volatile bool _running;
    private ApiRouter? _router;

    public void Start(int port, string webUiPath)
    {
        _ = AuthManager.Instance;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:{port}/");
        _router = new ApiRouter(webUiPath);
        _listener.Start();
        _running = true;
        _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "HttpApiServer" };
        _listenerThread.Start();
        AppLogger.Log($"[HttpApiServer] Started at http://localhost:{port}/");
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        _listenerThread?.Join(3000);
        AppLogger.Log("[HttpApiServer] Stopped");
    }

    private void ListenLoop()
    {
        while (_running)
        {
            try
            {
                var ctx = _listener!.GetContext();
                _ = Task.Run(() => HandleRequest(ctx));
            }
            catch (HttpListenerException) { break; }
            catch (Exception ex) { AppLogger.Warning($"[HttpApiServer] ListenLoop: {ex.Message}"); }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"]  = "*";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-API-Key";

            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

            await _router!.RouteAsync(ctx);
        }
        catch (Exception ex)
        {
            try { WriteError(ctx, 500, ex.Message); } catch { }
        }
    }

    public static void WriteError(HttpListenerContext ctx, int statusCode, string message)
    {
        var json = Json.ToJson(new ApiResult { success = false, error = message });
        WriteJson(ctx, statusCode, json);
    }

    public static void WriteJson(HttpListenerContext ctx, int statusCode, string json)
    {
        try
        {
            byte[] buf = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode        = statusCode;
            ctx.Response.ContentType       = "application/json; charset=utf-8";
            ctx.Response.ContentLength64   = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
        }
        finally { ctx.Response.Close(); }
    }
}
