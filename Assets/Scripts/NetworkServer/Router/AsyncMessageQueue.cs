using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;

/// <summary>
/// 非同步訊息佇列
/// </summary>
/// <typeparam name="T"></typeparam>
public class AsyncMessageQueue<T>
{
    private readonly ConcurrentQueue<T> queue = new();
    private readonly SemaphoreSlim semaphoreSlim = new(0);
    private const int MAX_QUEUE_SIZE = 10000; // 最大佇列大小
    private int currentCount = 0;

    /// <summary>
    /// 放入佇列
    /// </summary>
    /// <param name="item"></param>
    public void Enqueue(T item)
    {
        // 檢查佇列大小限制
        if (System.Threading.Interlocked.Increment(ref currentCount) > MAX_QUEUE_SIZE)
        {
            System.Threading.Interlocked.Decrement(ref currentCount);
            UnityEngine.Debug.LogWarning($"[安全] 訊息佇列已達到最大容量 {MAX_QUEUE_SIZE}，丟棄新訊息");
            return;
        }

        queue.Enqueue(item);
        semaphoreSlim.Release();
    }

    /// <summary>
    /// 取出佇列
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<T> DequeueAsync(CancellationToken cancellationToken = default)
    {
        await semaphoreSlim.WaitAsync(cancellationToken);
        if (queue.TryDequeue(out var item))
        {
            System.Threading.Interlocked.Decrement(ref currentCount);
        }
        return item;
    }

    /// <summary>
    /// 獲取當前佇列大小
    /// </summary>
    public int Count => currentCount;
}