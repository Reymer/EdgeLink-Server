using System;
using System.Threading;
using System.Threading.Tasks;

public static class SafeExecution
{
    /// <summary>
    /// 安全執行同步方法，捕獲異常
    /// </summary>
    public static void Safe(Action action, string context = "")
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[Safe:{context}] Exception: {ex}");
        }
    }

    /// <summary>
    /// 安全執行非同步方法，捕獲異常
    /// </summary>
    public static async Task SafeAsync(Func<Task> asyncAction, string context = "")
    {
        try
        {
            if (asyncAction != null)
            {
                await asyncAction();
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[SafeAsync:{context}] Exception: {ex}");
        }
    }

    /// <summary>
    /// 支援取消的 await 任務
    /// </summary>
    public static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
        return await task; // 這裡 task 一定已經完成了
    }

    /// <summary>
    /// 支援取消的 await 任務（無回傳值版）
    /// </summary>
    public static async Task WithCancellation(Task task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
        await task; // task 一定完成
    }
}
