using System.Threading.Tasks;
using System.Threading;
using System;

public static class TaskExtensions
{
    public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<object>();
        using (cancellationToken.Register(s => ((TaskCompletionSource<object>)s).TrySetResult(null), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task)) throw new OperationCanceledException(cancellationToken);
        }
        return await task;
    }
}