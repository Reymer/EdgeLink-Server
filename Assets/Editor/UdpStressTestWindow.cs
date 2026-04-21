using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UDP 高併發壓力測試視窗。
/// 開啟：Tools > UDP Stress Test
///
/// 流程：
///   本視窗 ──發送──▶ Connector 監聽端口 (RemotePort)
///   本視窗 ◀──接收── Connector 轉發端口 (LocalPort)
///
/// 每個封包帶有序號與時間戳，回收時計算往返延遲（RTT）。
/// 建議 Connector 使用 OriginalData 遮罩以保持封包內容不變。
/// </summary>
public class UdpStressTestWindow : EditorWindow
{
    // ── 設定 ──────────────────────────────────────────────────────────────────
    private string targetIp      = "127.0.0.1";
    private int    sendPort      = 565;   // Connector 監聽端口 (RemotePortDetails.Port)
    private int    listenPort    = 1234;  // Connector 轉發端口 (LocalPortDetails.Port)
    private int    packetsPerSec = 500;
    private int    durationSec   = 10;
    private string payload       = "ID:1;TEMP:25";

    // ── 狀態 ──────────────────────────────────────────────────────────────────
    private bool   _running;
    private CancellationTokenSource _cts;

    // ── 統計（Interlocked 操作） ──────────────────────────────────────────────
    private long   _sent;
    private long   _received;
    private long   _dropped;        // 超時未收到
    private long   _totalRttTicks;
    private long   _maxRttTicks;
    private long   _minRttTicks = long.MaxValue;

    // 已送出但未收到回應的封包：seq → send ticks
    private readonly ConcurrentDictionary<long, long> _pending = new();
    private long _seq;

    private const long TimeoutTicks = TimeSpan.TicksPerMillisecond * 2000; // 2 秒逾時

    private Vector2 _logScroll;
    private readonly ConcurrentQueue<string> _logs = new();
    private const int MaxLogLines = 100;

    // ── 視窗入口 ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/UDP Stress Test")]
    public static void Open() => GetWindow<UdpStressTestWindow>("UDP Stress Test");

    private void OnInspectorUpdate() => Repaint();

    private void OnDisable() => StopTest();

    // ── GUI ───────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        EditorGUILayout.LabelField("設定", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(_running))
        {
            targetIp      = EditorGUILayout.TextField("目標 IP",              targetIp);
            sendPort      = EditorGUILayout.IntField("發送到 (RemotePort)",    sendPort);
            listenPort    = EditorGUILayout.IntField("接收在 (LocalPort)",     listenPort);
            packetsPerSec = EditorGUILayout.IntField("封包/秒",                packetsPerSec);
            durationSec   = EditorGUILayout.IntField("持續秒數 (0=無限)",      durationSec);
            payload       = EditorGUILayout.TextField("訊息",                  payload);
        }

        EditorGUILayout.HelpBox(
            "請確認 Connector 使用 OriginalData 遮罩，否則時間戳欄位可能被轉換而無法計算 RTT。",
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
        long minTicks = Interlocked.Read(ref _minRttTicks);
        long maxTicks = Interlocked.Read(ref _maxRttTicks);
        long total    = Interlocked.Read(ref _totalRttTicks);

        double avgMs = received > 0
            ? total / (double)received / TimeSpan.TicksPerMillisecond
            : 0;
        double maxMs = maxTicks / (double)TimeSpan.TicksPerMillisecond;
        double minMs = minTicks == long.MaxValue ? 0
            : minTicks / (double)TimeSpan.TicksPerMillisecond;
        double lossRate = sent > 0 ? dropped * 100.0 / sent : 0;

        EditorGUILayout.LabelField($"已送出:    {sent:N0}");
        EditorGUILayout.LabelField($"已收到:    {received:N0}");
        EditorGUILayout.LabelField($"逾時掉包:  {dropped:N0}  ({lossRate:F1}%)");
        EditorGUILayout.LabelField($"RTT 最小:  {minMs:F2} ms");
        EditorGUILayout.LabelField($"RTT 平均:  {avgMs:F2} ms");
        EditorGUILayout.LabelField($"RTT 最大:  {maxMs:F2} ms");

        // ── 日誌 ──────────────────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("日誌", EditorStyles.boldLabel);
        _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(180));
        var lines = _logs.ToArray();
        foreach (var line in lines)
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
    }

    // ── 開始 / 停止 ───────────────────────────────────────────────────────────
    private void StartTest()
    {
        ResetStats();
        _running = true;
        _cts = new CancellationTokenSource();

        AddLog($"▶ 開始測試：送到 {targetIp}:{sendPort}，接收在 :{listenPort}，{packetsPerSec} pkt/s");

        Task.Run(() => ReceiverLoop(_cts.Token));
        Task.Run(() => SenderLoop(_cts.Token));

        if (durationSec > 0)
            Task.Delay(durationSec * 1000).ContinueWith(_ => StopTest());
    }

    private void StopTest()
    {
        if (!_running) return;
        _running = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // 統計最終摘要
        long sent     = Interlocked.Read(ref _sent);
        long received = Interlocked.Read(ref _received);
        long dropped  = Interlocked.Read(ref _dropped);
        long total    = Interlocked.Read(ref _totalRttTicks);
        double avgMs  = received > 0
            ? total / (double)received / TimeSpan.TicksPerMillisecond : 0;

        AddLog($"■ 測試結束 | 送:{sent}  收:{received}  掉:{dropped}  平均RTT:{avgMs:F2}ms");
    }

    // ── 發送迴圈 ──────────────────────────────────────────────────────────────
    private async Task SenderLoop(CancellationToken token)
    {
        using var udp = new UdpClient();
        var ep = new IPEndPoint(IPAddress.Parse(targetIp), sendPort);

        int intervalMs = packetsPerSec > 0 ? 1000 / packetsPerSec : 1;

        try
        {
            while (!token.IsCancellationRequested)
            {
                long seq   = Interlocked.Increment(ref _seq);
                long ticks = DateTime.UtcNow.Ticks;

                // 格式：SEQ:{n};TS:{ticks};{payload}
                string msg   = $"SEQ:{seq};TS:{ticks};{payload}";
                byte[] bytes = Encoding.UTF8.GetBytes(msg);

                _pending[seq] = ticks;
                await udp.SendAsync(bytes, bytes.Length, ep);
                Interlocked.Increment(ref _sent);

                // 每 100 個封包清理一次已逾時的待確認項目
                if (seq % 100 == 0) PurgeExpired();

                await Task.Delay(intervalMs, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AddLog($"[Sender] 錯誤: {ex.Message}"); }
    }

    // ── 接收迴圈 ──────────────────────────────────────────────────────────────
    private async Task ReceiverLoop(CancellationToken token)
    {
        UdpClient udp;
        try
        {
            udp = new UdpClient(listenPort);
        }
        catch (Exception ex)
        {
            AddLog($"[Receiver] 無法監聽端口 {listenPort}: {ex.Message}");
            return;
        }

        using (udp)
        {
            token.Register(() => udp.Close());
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var result = await udp.ReceiveAsync();
                    string msg = Encoding.UTF8.GetString(result.Buffer);
                    ProcessResponse(msg);
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
            catch (Exception ex) { AddLog($"[Receiver] 錯誤: {ex.Message}"); }
        }
    }

    // ── 解析回應並計算 RTT ────────────────────────────────────────────────────
    private void ProcessResponse(string msg)
    {
        // 從回應中找 SEQ 和 TS 欄位
        long seq   = ParseField(msg, "SEQ:");
        long tsTx  = ParseField(msg, "TS:");
        if (seq <= 0 || tsTx <= 0) return;

        if (!_pending.TryRemove(seq, out _)) return; // 已逾時或重複

        long rttTicks = DateTime.UtcNow.Ticks - tsTx;
        Interlocked.Increment(ref _received);
        Interlocked.Add(ref _totalRttTicks, rttTicks);

        // 更新最大值
        long cur;
        do { cur = Interlocked.Read(ref _maxRttTicks); }
        while (rttTicks > cur && Interlocked.CompareExchange(ref _maxRttTicks, rttTicks, cur) != cur);

        // 更新最小值
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
            {
                if (_pending.TryRemove(kv.Key, out _))
                    Interlocked.Increment(ref _dropped);
            }
        }
    }

    // ── 工具 ─────────────────────────────────────────────────────────────────
    private void ResetStats()
    {
        _sent = _received = _dropped = _totalRttTicks = _maxRttTicks = 0;
        _minRttTicks = long.MaxValue;
        _seq = 0;
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
