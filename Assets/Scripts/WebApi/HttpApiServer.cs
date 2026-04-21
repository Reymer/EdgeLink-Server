using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class HttpApiServer
{
    private HttpListener _listener;
    private Thread _listenerThread;
    private volatile bool _running;
    private ApiRouter _router;

    public void Start(int port, string webUiPath)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _router = new ApiRouter(webUiPath);
        _listener.Start();
        _running = true;
        _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "HttpApiServer" };
        _listenerThread.Start();
        Debug.Log($"[HttpApiServer] Started at http://localhost:{port}/");
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        _listenerThread?.Join(3000);
        Debug.Log("[HttpApiServer] Stopped");
    }

    private void ListenLoop()
    {
        while (_running)
        {
            try
            {
                var ctx = _listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
            }
            catch (HttpListenerException)
            {
                break; // Stop() 觸發
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HttpApiServer] ListenLoop exception: {ex.Message}");
            }
        }
    }

    private async void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

            await _router.RouteAsync(ctx);
        }
        catch (Exception ex)
        {
            try
            {
                WriteError(ctx, 500, ex.Message);
            }
            catch { }
        }
    }

    public static void WriteError(HttpListenerContext ctx, int statusCode, string message)
    {
        var result = new ApiResult { success = false, error = message };
        WriteJson(ctx, statusCode, UnityEngine.JsonUtility.ToJson(result));
    }

    public static void WriteJson(HttpListenerContext ctx, int statusCode, string json)
    {
        try
        {
            byte[] buf = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
        }
        finally
        {
            ctx.Response.OutputStream.Close();
        }
    }
}
