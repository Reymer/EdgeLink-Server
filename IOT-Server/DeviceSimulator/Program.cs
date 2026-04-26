using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// EdgeLink IoT 設備模擬器
/// 模擬一台 IoT 設備連接到 EdgeLink Server 的 TCP Server 端口，定時傳送假資料。
///
/// 使用方式：
///   DeviceSimulator [host] [port] [interval_ms] [device_id]
///
/// 範例：
///   DeviceSimulator 127.0.0.1 8888 1000 DEV-01
/// </summary>
class Program
{
    const string PingPrefix = "EDGELINK_PING:";

    static async Task Main(string[] args)
    {
        string host       = args.Length > 0 ? args[0] : "127.0.0.1";
        int    port       = args.Length > 1 ? int.Parse(args[1]) : 8888;
        int    intervalMs = args.Length > 2 ? int.Parse(args[2]) : 1000;
        string deviceId   = args.Length > 3 ? args[3] : "DEV-01";

        Console.WriteLine($"[模擬器] 設備 ID : {deviceId}");
        Console.WriteLine($"[模擬器] 目標    : {host}:{port}");
        Console.WriteLine($"[模擬器] 傳送間隔: {intervalMs} ms");
        Console.WriteLine($"[模擬器] 按 Q 斷線，按 R 重連，按 Ctrl+C 結束");
        Console.WriteLine(new string('─', 50));

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        while (!cts.Token.IsCancellationRequested)
        {
            await RunSessionAsync(host, port, intervalMs, deviceId, cts.Token);

            if (!cts.Token.IsCancellationRequested)
            {
                Console.WriteLine("[模擬器] 5 秒後重新連線...");
                try { await Task.Delay(5000, cts.Token); } catch (OperationCanceledException) { }
            }
        }

        Console.WriteLine("[模擬器] 已結束。");
    }

    static async Task RunSessionAsync(string host, int port, int intervalMs, string deviceId, CancellationToken token)
    {
        using var client = new TcpClient();
        try
        {
            Console.WriteLine($"[模擬器] 連線中 {host}:{port} ...");
            await client.ConnectAsync(host, port);
            client.NoDelay = true;
            Console.WriteLine($"[模擬器] 已連線");

            var stream  = client.GetStream();
            var sendCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            var readTask  = ReadLoopAsync(stream, sendCts.Token);
            var writeTask = WriteLoopAsync(stream, intervalMs, deviceId, sendCts.Token);
            var keyTask   = KeyLoopAsync(sendCts);

            await Task.WhenAny(readTask, writeTask, keyTask);
            sendCts.Cancel();
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[模擬器] 連線失敗：{ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[模擬器] 錯誤：{ex.Message}");
        }
        finally
        {
            Console.WriteLine("[模擬器] 已斷線");
        }
    }

    static async Task ReadLoopAsync(NetworkStream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                // 回應 PING
                if (line.StartsWith(PingPrefix, StringComparison.Ordinal))
                {
                    string ticks = line.Substring(PingPrefix.Length);
                    await SendLineAsync(stream, $"EDGELINK_PONG:{ticks}", token);
                    Console.WriteLine($"[模擬器] ↔ PING/PONG");
                    continue;
                }

                // 收到 EdgeLink 下發的指令
                Console.WriteLine($"[模擬器] ← 指令：{line}");
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    static async Task WriteLoopAsync(NetworkStream stream, int intervalMs, string deviceId, CancellationToken token)
    {
        var rng = new Random();
        int seq = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                seq++;
                double temp  = Math.Round(20.0 + rng.NextDouble() * 15.0, 1);
                double humi  = Math.Round(40.0 + rng.NextDouble() * 40.0, 1);
                int    light = rng.Next(100, 1000);

                string data = $"ID:{deviceId};SEQ:{seq};TEMP:{temp};HUMI:{humi};LIGHT:{light}";
                await SendLineAsync(stream, data, token);
                Console.WriteLine($"[模擬器] → {data}");

                await Task.Delay(intervalMs, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    static async Task KeyLoopAsync(CancellationTokenSource cts)
    {
        await Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (!Console.KeyAvailable) { await Task.Delay(100); continue; }
                var key = Console.ReadKey(intercept: true).Key;
                if (key == ConsoleKey.Q || key == ConsoleKey.R)
                {
                    Console.WriteLine(key == ConsoleKey.Q ? "\n[模擬器] 手動斷線" : "\n[模擬器] 重新連線");
                    cts.Cancel();
                }
            }
        });
    }

    static async Task SendLineAsync(NetworkStream stream, string line, CancellationToken token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes, token);
    }
}
