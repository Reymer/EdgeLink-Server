using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class MonitorSseHandler
{
    // 佇列上限：瀏覽器跟不上時丟棄最新訊息，避免記憶體無限成長
    private const int MAX_QUEUE = 200;

    // SSE 最高推送頻率：每個 client 每 50ms 最多送一批（20 batch/s）
    // 高負載時瀏覽器仍可正常回應，不會因 DOM 更新過快而凍結
    private const int MIN_FLUSH_INTERVAL_MS = 50;

    private class Client
    {
        public readonly ConcurrentQueue<string> Queue = new();
        public readonly SemaphoreSlim Signal = new SemaphoreSlim(0, int.MaxValue);
        public int Count; // Interlocked 計數，O(1) 大小查詢
    }

    private static readonly ConcurrentDictionary<Guid, Client> _clients = new();

    // 全域取樣計數：無 SSE client 連線時完全跳過，有 client 時限制入隊速率
    private static long _lastEnqueueTick = 0;
    private const long MIN_ENQUEUE_INTERVAL_TICKS = TimeSpan.TicksPerMillisecond * 20; // 最多 50 msg/s 進佇列

    public static void Publish(string message)
    {
        if (_clients.IsEmpty) return;

        // 全域 rate limit：CAS 確保只有一個執行緒能通過，避免多執行緒同時繞過限制
        long now = DateTime.UtcNow.Ticks;
        long last = Interlocked.Read(ref _lastEnqueueTick);
        if (now - last < MIN_ENQUEUE_INTERVAL_TICKS) return;
        if (Interlocked.CompareExchange(ref _lastEnqueueTick, now, last) != last) return;

        foreach (var kv in _clients)
        {
            var c = kv.Value;
            if (Interlocked.Increment(ref c.Count) > MAX_QUEUE)
            {
                Interlocked.Decrement(ref c.Count);
                continue;
            }
            c.Queue.Enqueue(message);
            c.Signal.Release();
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
        var sb = new StringBuilder();

        try
        {
            while (true)
            {
                // 等待至少一筆訊息
                await client.Signal.WaitAsync();

                // 批次 drain 佇列 → 合併成一次寫入 + 一次 flush
                sb.Clear();
                while (client.Queue.TryDequeue(out var msg))
                {
                    Interlocked.Decrement(ref client.Count);
                    sb.Append("data: ").Append(msg).Append("\n\n");
                }

                if (sb.Length == 0) continue;

                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                await stream.WriteAsync(bytes, 0, bytes.Length);
                await stream.FlushAsync();

                // 送完後等一段時間再繼續，讓瀏覽器有時間渲染
                await Task.Delay(MIN_FLUSH_INTERVAL_MS);

                // drain 等待期間累積的訊號，避免下次 WaitAsync 立刻回傳
                while (client.Signal.CurrentCount > 0)
                    client.Signal.Wait(0);
            }
        }
        catch
        {
            // client 斷線
        }
        finally
        {
            _clients.TryRemove(id, out _);
            try { stream.Close(); } catch { }
        }
    }
}
