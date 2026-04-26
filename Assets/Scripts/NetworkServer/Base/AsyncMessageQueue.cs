using System.Collections.Concurrent;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 非同步訊息佇列
/// </summary>
public class AsyncMessageQueue<T>
{
    private readonly ConcurrentQueue<T> queue = new();
    private readonly SemaphoreSlim semaphoreSlim = new(0);
    private readonly int maxQueueSize;
    private volatile int currentCount = 0;

    public AsyncMessageQueue(int maxSize = 10000)
    {
        maxQueueSize = maxSize;
    }

    public void Enqueue(T item)
    {
        if (System.Threading.Interlocked.Increment(ref currentCount) > maxQueueSize)
        {
            System.Threading.Interlocked.Decrement(ref currentCount);
            UnityEngine.Debug.LogWarning($"[安全] 訊息佇列已達到最大容量 {maxQueueSize}，丟棄新訊息");
            return;
        }

        queue.Enqueue(item);
        semaphoreSlim.Release();
    }

    public async UniTask<T> DequeueAsync(CancellationToken cancellationToken = default)
    {
        await semaphoreSlim.WaitAsync(cancellationToken);

        if (queue.TryDequeue(out var item))
        {
            System.Threading.Interlocked.Decrement(ref currentCount);
            return item;
        }

        // semaphore 放行但佇列為空 — invariant 被破壞，還原計數避免後續全部卡死
        semaphoreSlim.Release();
        UnityEngine.Debug.LogError("[AsyncMessageQueue] semaphore/queue 計數不一致，已自動還原");
        return default;
    }

    public int Count => currentCount;

    /// <summary>
    /// 清空佇列並歸零 semaphore，用於重連前丟棄舊 session 的殘留請求。
    /// 呼叫時須確保無其他 Enqueue/DequeueAsync 同時進行。
    /// </summary>
    public void Clear()
    {
        while (queue.TryDequeue(out _))
            System.Threading.Interlocked.Decrement(ref currentCount);
        while (semaphoreSlim.CurrentCount > 0)
            semaphoreSlim.Wait(0);
    }
}
