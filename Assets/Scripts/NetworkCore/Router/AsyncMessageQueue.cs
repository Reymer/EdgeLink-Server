using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;

public class AsyncMessageQueue<T>
{
    private readonly ConcurrentQueue<T> queue = new();
    private readonly SemaphoreSlim semaphoreSlim = new(0);

    /// <summary>
    /// 放入佇列
    /// </summary>
    /// <param name="item"></param>
    public void Enqueue(T item)
    {
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
        queue.TryDequeue(out var item);
        return item;
    }
}