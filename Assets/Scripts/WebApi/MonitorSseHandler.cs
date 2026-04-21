using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class MonitorSseHandler
{
    private class Client
    {
        public readonly ConcurrentQueue<string> Queue = new();
        public readonly SemaphoreSlim Signal = new SemaphoreSlim(0, int.MaxValue);
    }

    private static readonly ConcurrentDictionary<Guid, Client> _clients = new();

    public static void Publish(string message)
    {
        foreach (var kv in _clients)
        {
            kv.Value.Queue.Enqueue(message);
            kv.Value.Signal.Release();
        }
    }

    public async Task HandleAsync(HttpListenerContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
        ctx.Response.SendChunked = true;

        var id = Guid.NewGuid();
        var client = new Client();
        _clients[id] = client;
        var stream = ctx.Response.OutputStream;

        try
        {
            while (true)
            {
                await client.Signal.WaitAsync();
                while (client.Queue.TryDequeue(out var msg))
                {
                    var bytes = Encoding.UTF8.GetBytes($"data: {msg}\n\n");
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                    await stream.FlushAsync();
                }
            }
        }
        catch
        {
            // client disconnected
        }
        finally
        {
            _clients.TryRemove(id, out _);
            try { stream.Close(); } catch { }
        }
    }
}
