using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TCP 高併發壓力測試視窗。
/// 開啟：Tools > TCP Stress Test
///
/// 流程：
///   本視窗 ──發送──▶ IoT TCPServer (ServerPort)   [本視窗 = TCP Client]
///   本視窗 ◀──接收── IoT TCPClient 的轉發目標       [本視窗 = TCP Server，監聽 ListenPort]
///
/// 設定步驟：
///   1. IoT Server 建立一個 TCPServer，LocalPort = ServerPort
///   2. IoT Server 建立一個 TCPClient，TargetIP = 127.0.0.1，RemotePort = ListenPort
///   3. 兩者 ProtocolName 相同，Router 才會配對轉發
///   4. 本視窗按「開始」，TCPClient 會自動連進來
/// </summary>
public class TcpStressTestWindow : EditorWindow
{
    // ── 設定 ──────────────────────────────────────────────────────────────────
    private string targetIp      = "127.0.0.1";
    private int    serverPort    = 565;    // IoT TCPServer 的 LocalPort（本視窗連過去）
    private int    listenPort    = 1234;   // 本視窗開的 TCP Server（IoT TCPClient 連進來）
    private int    packetsPerSec = 500;
    private int    durationSec   = 10;
    private string payload       = "ID:1;TEMP:25";

    // ── 狀態 ──────────────────────────────────────────────────────────────────
    private bool   _running;
    private bool   _senderConnected;
    private int    _receiverClients;
    private CancellationTokenSource _cts;           // 控制發送端（每次測試重建）
    private CancellationTokenSource _receiverCts;   // 控制接收端（視窗存活期間不關閉）
    private int    _activeListenPort = -1;

    // ── 統計 ──────────────────────────────────────────────────────────────────
    private long _sent;
    private long _received;
    private long _dropped;
    private long _totalRttTicks;
    private long _maxRttTicks;
    private long _minRttTicks = long.MaxValue;
    private long _sendErrors;

    private readonly ConcurrentDictionary<long, long> _pending = new();
    private long _seq;
    private const long TimeoutTicks = TimeSpan.TicksPerMillisecond * 2000;

    private Vector2 _logScroll;
    private readonly ConcurrentQueue<string> _logs = new();
    private const int MaxLogLines = 100;

    // ── 視窗入口 ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/TCP Stress Test")]
    public static void Open() => GetWindow<TcpStressTestWindow>("TCP Stress Test");

    private void OnInspectorUpdate() => Repaint();
    private void OnDisable()
    {
        StopTest();
        _receiverCts?.Cancel();
        _receiverCts?.Dispose();
        _receiverCts = null;
        _activeListenPort = -1;
    }

    // ── GUI ───────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        EditorGUILayout.LabelField("設定", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(_running))
        {
            targetIp      = EditorGUILayout.TextField("目標 IP",                    targetIp);
            serverPort    = EditorGUILayout.IntField("發送到 (TCPServer LocalPort)", serverPort);
            listenPort    = EditorGUILayout.IntField("接收在 (本視窗監聽 Port)",     listenPort);
            packetsPerSec = EditorGUILayout.IntField("封包/秒",                      packetsPerSec);
            durationSec   = EditorGUILayout.IntField("持續秒數 (0=無限)",            durationSec);
            payload       = EditorGUILayout.TextField("訊息",                        payload);
        }

        EditorGUILayout.HelpBox(
            "本視窗同時扮演兩個角色：\n" +
            "① TCP Client → 連到 IoT TCPServer（ServerPort）送封包\n" +
            "② TCP Server → 監聽 ListenPort，等 IoT TCPClient 連進來並接收轉發封包\n\n" +
            "確認 IoT TCPClient 的 TargetIP=127.0.0.1、RemotePort=ListenPort，且 OriginalData 遮罩。",
            MessageType.Info);

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_running))
                if (GUILayout.Button("▶ 開始", GUILayout.Height(30))) StartTest();
            using (new EditorGUI.DisabledScope(!_running))
                if (GUILayout.Button("■ 停止", GUILayout.Height(30))) StopTest();
        }

        // ── 即時統計 ──────────────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("即時統計", EditorStyles.boldLabel);

        long sent     = Interlocked.Read(ref _sent);
        long received = Interlocked.Read(ref _received);
        long dropped  = Interlocked.Read(ref _dropped);
        long errors   = Interlocked.Read(ref _sendErrors);
        long minTicks = Interlocked.Read(ref _minRttTicks);
        long maxTicks = Interlocked.Read(ref _maxRttTicks);
        long total    = Interlocked.Read(ref _totalRttTicks);

        double avgMs = received > 0
            ? total / (double)received / TimeSpan.TicksPerMillisecond : 0;
        double maxMs = maxTicks / (double)TimeSpan.TicksPerMillisecond;
        double minMs = minTicks == long.MaxValue ? 0
            : minTicks / (double)TimeSpan.TicksPerMillisecond;
        double lossRate = sent > 0 ? dropped * 100.0 / sent : 0;

        string connStatus  = _senderConnected ? "✓ 已連接" : "✗ 未連接";
        string rcvStatus   = _receiverClients > 0 ? $"✓ {_receiverClients} 個連線" : "✗ 無連線";

        EditorGUILayout.LabelField($"送出端連線:   {connStatus}");
        EditorGUILayout.LabelField($"接收端連線:   {rcvStatus}");
        EditorGUILayout.LabelField($"已送出:       {sent:N0}");
        EditorGUILayout.LabelField($"已收到:       {received:N0}");
        EditorGUILayout.LabelField($"逾時掉包:     {dropped:N0}  ({lossRate:F1}%)");
        EditorGUILayout.LabelField($"發送錯誤:     {errors:N0}");
        EditorGUILayout.LabelField($"RTT 最小:     {minMs:F2} ms");
        EditorGUILayout.LabelField($"RTT 平均:     {avgMs:F2} ms");
        EditorGUILayout.LabelField($"RTT 最大:     {maxMs:F2} ms");

        // ── 日誌 ──────────────────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("日誌", EditorStyles.boldLabel);
        _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(180));
        foreach (var line in _logs.ToArray())
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
    }

    // ── 開始 / 停止 ───────────────────────────────────────────────────────────
    private void StartTest()
    {
        ResetStats();
        _running = true;
        _cts = new CancellationTokenSource();

        // 接收端 Listener 只在 port 變動或首次啟動時重建，其他情況持續複用
        if (_activeListenPort != listenPort || _receiverCts == null || _receiverCts.IsCancellationRequested)
        {
            _receiverCts?.Cancel();
            _receiverCts?.Dispose();
            _receiverCts = new CancellationTokenSource();
            _activeListenPort = listenPort;
            _receiverClients = 0;
            Task.Run(() => ReceiverLoop(_receiverCts.Token));
            AddLog($"▶ 開始：送到 {targetIp}:{serverPort}，接收在 :{listenPort}，{packetsPerSec} pkt/s");
        }
        else
        {
            AddLog($"▶ 開始：送到 {targetIp}:{serverPort}，接收在 :{listenPort}（複用連線），{packetsPerSec} pkt/s");
        }

        Task.Run(() => SenderLoop(_cts.Token));

        if (durationSec > 0)
            Task.Delay(durationSec * 1000).ContinueWith(_ => StopTest());
    }

    private void StopTest()
    {
        if (!_running) return;
        _running = false;
        _senderConnected = false;
        // 不重置 _receiverClients：接收端持續運行，連線數保持有效
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        long sent     = Interlocked.Read(ref _sent);
        long received = Interlocked.Read(ref _received);
        long dropped  = Interlocked.Read(ref _dropped);
        long total    = Interlocked.Read(ref _totalRttTicks);
        double avgMs  = received > 0
            ? total / (double)received / TimeSpan.TicksPerMillisecond : 0;

        AddLog($"■ 結束 | 送:{sent}  收:{received}  掉:{dropped}  平均RTT:{avgMs:F2}ms");
    }

    // ── 發送迴圈（本視窗作為 TCP Client）──────────────────────────────────────
    private async Task SenderLoop(CancellationToken token)
    {
        int intervalMs = packetsPerSec > 0 ? 1000 / packetsPerSec : 1;
        TcpClient tcp = null;

        try
        {
            tcp = new TcpClient { NoDelay = true };
            await tcp.ConnectAsync(targetIp, serverPort);
            _senderConnected = true;
            AddLog($"[Sender] TCP 已連接到 {targetIp}:{serverPort}");

            var stream = tcp.GetStream();

            // 等待 IoT TCPClient 連入接收端，確保路由路徑就緒再發送
            AddLog("[Sender] 等待接收端連線（最多 35 秒）…");
            int waitedMs = 0;
            int nextLogMs = 5000;
            while (_receiverClients == 0 && !token.IsCancellationRequested)
            {
                await Task.Delay(50, token);
                waitedMs += 50;
                if (waitedMs >= nextLogMs)
                {
                    AddLog($"[Sender] 仍在等待 IoT TCPClient 連入… ({waitedMs / 1000}s)");
                    nextLogMs += 5000;
                }
                if (waitedMs >= 35000)
                {
                    AddLog("[Sender] 等待逾時：IoT TCPClient 尚未連入。若為重複測試，請等 TCPClient 完成重連再試。");
                    return;
                }
            }

            if (token.IsCancellationRequested) return;
            AddLog($"[Sender] 接收端就緒（等待 {waitedMs} ms），開始發送");

            while (!token.IsCancellationRequested)
            {
                long seq   = Interlocked.Increment(ref _seq);
                long ticks = DateTime.UtcNow.Ticks;

                // 格式：SEQ:{n};TS:{ticks};{payload}\n
                string msg   = $"SEQ:{seq};TS:{ticks};{payload}\n";
                byte[] bytes = Encoding.UTF8.GetBytes(msg);

                _pending[seq] = ticks;

                try
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length, token);
                    Interlocked.Increment(ref _sent);
                }
                catch (Exception)
                {
                    _pending.TryRemove(seq, out _);
                    Interlocked.Increment(ref _sendErrors);
                }

                if (seq % 100 == 0) PurgeExpired();

                await Task.Delay(intervalMs, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _senderConnected = false;
            Interlocked.Increment(ref _sendErrors);
            AddLog($"[Sender] 連線錯誤: {ex.Message}");
        }
        finally
        {
            _senderConnected = false;
            try { tcp?.Close(); } catch { }
        }
    }

    // ── 接收迴圈（本視窗作為 TCP Server，等待 IoT TCPClient 連入）────────────
    private async Task ReceiverLoop(CancellationToken token)
    {
        TcpListener listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, listenPort);
            listener.Start();
            AddLog($"[Receiver] TCP Server 監聽 :{listenPort}，等待 IoT TCPClient 連入…");

            while (!token.IsCancellationRequested)
            {
                // 用 WhenAny 讓 AcceptTcpClientAsync 可以被取消
                var acceptTask  = listener.AcceptTcpClientAsync();
                var cancelDelay = Task.Delay(Timeout.Infinite, token);

                if (await Task.WhenAny(acceptTask, cancelDelay) != acceptTask)
                {
                    _ = acceptTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    break;
                }

                var client = await acceptTask;
                Interlocked.Increment(ref _receiverClients);
                AddLog($"[Receiver] IoT TCPClient 已連入（{client.Client.RemoteEndPoint}）");

                _ = Task.Run(() => HandleIncomingClient(client, token), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AddLog($"[Receiver] 錯誤: {ex.Message}"); }
        finally
        {
            try { listener?.Stop(); } catch { }
        }
    }

    private async Task HandleIncomingClient(TcpClient client, CancellationToken token)
    {
        try
        {
            using (client)
            using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8))
            {
                string line;
                while (!token.IsCancellationRequested &&
                       (line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        ProcessResponse(line);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            Interlocked.Decrement(ref _receiverClients);
        }
    }

    // ── 解析回應並計算 RTT ────────────────────────────────────────────────────
    private void ProcessResponse(string msg)
    {
        long seq  = ParseField(msg, "SEQ:");
        long tsTx = ParseField(msg, "TS:");
        if (seq <= 0 || tsTx <= 0) return;
        if (!_pending.TryRemove(seq, out _)) return;

        long rttTicks = DateTime.UtcNow.Ticks - tsTx;
        Interlocked.Increment(ref _received);
        Interlocked.Add(ref _totalRttTicks, rttTicks);

        long cur;
        do { cur = Interlocked.Read(ref _maxRttTicks); }
        while (rttTicks > cur && Interlocked.CompareExchange(ref _maxRttTicks, rttTicks, cur) != cur);

        do { cur = Interlocked.Read(ref _minRttTicks); }
        while (rttTicks < cur && Interlocked.CompareExchange(ref _minRttTicks, rttTicks, cur) != cur);
    }

    private static long ParseField(string msg, string key)
    {
        int idx = msg.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return -1;
        int start = idx + key.Length;
        int end   = msg.IndexOf(';', start);
        string raw = end < 0 ? msg.Substring(start) : msg.Substring(start, end - start);
        return long.TryParse(raw, out long v) ? v : -1;
    }

    // ── 清理逾時封包 ──────────────────────────────────────────────────────────
    private void PurgeExpired()
    {
        long now = DateTime.UtcNow.Ticks;
        foreach (var kv in _pending)
        {
            if (now - kv.Value > TimeoutTicks)
                if (_pending.TryRemove(kv.Key, out _))
                    Interlocked.Increment(ref _dropped);
        }
    }

    // ── 工具 ──────────────────────────────────────────────────────────────────
    private void ResetStats()
    {
        _sent = _received = _dropped = _totalRttTicks = _maxRttTicks = _sendErrors = 0;
        _minRttTicks = long.MaxValue;
        _seq = 0;
        _senderConnected = false;
        // _receiverClients 不重置：接收端持續運行
        _pending.Clear();
        while (_logs.TryDequeue(out _)) { }
    }

    private void AddLog(string line)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        _logs.Enqueue($"[{ts}] {line}");
        while (_logs.Count > MaxLogLines && _logs.TryDequeue(out _)) { }
    }
}
