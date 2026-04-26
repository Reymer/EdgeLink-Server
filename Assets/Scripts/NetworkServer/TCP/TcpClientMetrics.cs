using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;

public class TcpClientMetrics
{
    public readonly IPEndPoint EndPoint;
    public readonly DateTime ConnectedAt = DateTime.UtcNow;

    private long messageCount = 0;
    private long totalBytes = 0;
    private long lastActivityTicks;

    private long windowBytes = 0;
    private long windowStartTicks;
    private double lastCompletedRate = 0;
    private readonly object rateLock = new();

    private long lastRttMsBits = -1L; // BitConverter.DoubleToInt64Bits(-1.0)
    private readonly ConcurrentDictionary<long, long> pendingPings = new();

    private const string PingPrefix = "EDGELINK_PING:";
    public const string PongPrefix  = "EDGELINK_PONG:";

    public TcpClientMetrics(IPEndPoint endPoint)
    {
        EndPoint = endPoint;
        long now = DateTime.UtcNow.Ticks;
        lastActivityTicks = now;
        windowStartTicks  = now;
        lastRttMsBits = BitConverter.DoubleToInt64Bits(-1.0);
    }

    public void RecordBytes(int count)
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        Interlocked.Add(ref totalBytes, count);
        Interlocked.Exchange(ref lastActivityTicks, nowTicks);

        lock (rateLock)
        {
            double elapsed = TimeSpan.FromTicks(nowTicks - windowStartTicks).TotalSeconds;
            if (elapsed >= 5.0)
            {
                lastCompletedRate = windowBytes / elapsed;
                windowBytes       = 0;
                windowStartTicks  = nowTicks;
            }
            windowBytes += count;
        }
    }

    public void RecordMessage() =>
        Interlocked.Increment(ref messageCount);

    public double GetRateBytesPerSec()
    {
        lock (rateLock)
        {
            double elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - windowStartTicks).TotalSeconds;
            if (elapsed < 0.5) return lastCompletedRate;
            return windowBytes / Math.Max(elapsed, 0.1);
        }
    }

    public long   GetMessageCount()       => Interlocked.Read(ref messageCount);
    public long   GetTotalBytes()         => Interlocked.Read(ref totalBytes);
    public double GetConnectedSeconds()   => (DateTime.UtcNow - ConnectedAt).TotalSeconds;
    public double GetLastActivitySeconds() =>
        TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref lastActivityTicks)).TotalSeconds;
    public double GetLastRttMs() =>
        BitConverter.Int64BitsToDouble(Interlocked.Read(ref lastRttMsBits));

    public string BuildPingMessage()
    {
        long ticks = DateTime.UtcNow.Ticks;
        pendingPings[ticks] = ticks;

        long cutoff = ticks - TimeSpan.FromSeconds(15).Ticks;
        foreach (var key in pendingPings.Keys.Where(k => k < cutoff).ToList())
            pendingPings.TryRemove(key, out _);

        return $"{PingPrefix}{ticks:X16}\n";
    }

    public bool TryHandlePong(string line)
    {
        if (!line.StartsWith(PongPrefix, StringComparison.Ordinal)) return false;
        if (long.TryParse(line.Substring(PongPrefix.Length), System.Globalization.NumberStyles.HexNumber, null, out long ticks)
            && pendingPings.TryRemove(ticks, out long sentTicks))
        {
            double rtt = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - sentTicks).TotalMilliseconds;
            Interlocked.Exchange(ref lastRttMsBits, BitConverter.DoubleToInt64Bits(rtt));
        }
        return true;
    }

    /// <summary>
    /// 連續 missedThreshold 個 PING 未收到 PONG，視為設備無回應（掉電或網路中斷）。
    /// </summary>
    public bool IsUnresponsive(int missedThreshold = 3) => pendingPings.Count >= missedThreshold;
}
