using System;
using System.Threading;
using System.Threading.Tasks;
using EdgeLink;

/// <summary>
/// EdgeLink 接收端測試主控台
/// 模擬 Unity 應用的接收端，用於在沒有 Unity 的情況下測試 EdgeLinkReceiver。
///
/// 使用方式：
///   ReceiverConsole [port] [mask_json]
///
/// 範例：
///   ReceiverConsole 9090
///   ReceiverConsole 9090 sensor.json
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        int    listenPort = args.Length > 0 ? int.Parse(args[0]) : 9090;
        string maskFile   = args.Length > 1 ? args[1] : null;

        Console.WriteLine("┌─────────────────────────────────────────┐");
        Console.WriteLine("│       EdgeLink Receiver Console         │");
        Console.WriteLine("└─────────────────────────────────────────┘");
        Console.WriteLine($"  監聽 Port : {listenPort}");
        Console.WriteLine($"  遮罩檔案  : {maskFile ?? "（未載入，顯示原始字串）"}");
        Console.WriteLine($"  按 S 輸入指令傳送，按 Ctrl+C 結束");
        Console.WriteLine(new string('─', 45));

        // 載入遮罩（選填）
        MaskDefinition mask = null;
        if (maskFile != null && System.IO.File.Exists(maskFile))
        {
            try
            {
                mask = MaskDefinition.FromJsonFile(maskFile);
                Console.WriteLine($"[Receiver] 遮罩載入：{mask.maskId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Receiver] 遮罩載入失敗：{ex.Message}");
            }
        }

        using var receiver = new EdgeLinkReceiver(Protocol.TCP, mask);
        using var cts      = new CancellationTokenSource();

        // ── 事件 ────────────────────────────────────────────────────
        receiver.OnConnectionChanged += connected =>
        {
            string tag = connected ? "✔ 已連線" : "✘ 已斷線";
            Console.WriteLine($"\n[Server ]  EdgeLink Server {tag}");
        };

        receiver.OnDeviceStatusChanged += (portName, endpoint, connected) =>
        {
            string tag = connected ? "▲ 上線" : "▼ 離線";
            Console.WriteLine($"\n[Device ]  [{portName}] {endpoint}  {tag}");
        };

        receiver.OnMessage += msg =>
        {
            Console.WriteLine($"[Message]  {msg.Raw}");

            if (msg.Parsed != null && msg.Parsed.Success && msg.Parsed.Fields.Count > 0)
            {
                foreach (var kv in msg.Parsed.Fields)
                    Console.Write($"  {kv.Key}={kv.Value}");
                Console.WriteLine();
            }
        };

        receiver.OnError += ex =>
            Console.WriteLine($"[Error  ]  {ex.Message}");

        // ── 啟動 ────────────────────────────────────────────────────
        receiver.Start(listenPort);
        Console.WriteLine($"[Receiver] 等待 EdgeLink Server 連入...\n");

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // Flush loop（取代 Unity 的 Update）
        var flushTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                receiver.Flush();
                await Task.Delay(16); // ~60fps
            }
        });

        // 鍵盤輸入（送出指令）
        await KeyLoopAsync(receiver, cts.Token);

        cts.Cancel();
        await flushTask;
        Console.WriteLine("\n[Receiver] 已結束。");
    }

    static async Task KeyLoopAsync(EdgeLinkReceiver receiver, CancellationToken token)
    {
        await Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                if (!Console.KeyAvailable) { await Task.Delay(100); continue; }

                var key = Console.ReadKey(intercept: true).Key;
                if (key == ConsoleKey.S)
                {
                    Console.Write("\n[Send   ]  輸入指令：");
                    string cmd = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(cmd))
                    {
                        await receiver.SendAsync(cmd);
                        Console.WriteLine($"[Send   ]  已送出：{cmd}");
                    }
                }
            }
        });
    }
}
