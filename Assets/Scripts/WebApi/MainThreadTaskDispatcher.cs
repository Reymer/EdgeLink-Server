using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

/// <summary>
/// 讓背景執行緒（HTTP handler）可以 await 一個在 Unity 主執行緒執行的操作。
/// 回傳 Task<T> 以維持 WebApi 層的相容性。
/// </summary>
public static class MainThreadTaskDispatcher
{
    public static Task<T> RunOnMainThread<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        UniTask.Post(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, PlayerLoopTiming.Update);
        return tcs.Task;
    }

    public static Task RunOnMainThread(Action action) =>
        RunOnMainThread<bool>(() => { action(); return true; });

    /// <summary>Runs an async operation that starts on the main thread.</summary>
    public static Task RunOnMainThread(Func<Task> func)
    {
        var tcs = new TaskCompletionSource<bool>();
        UniTask.Post(() =>
        {
            func().ContinueWith(t =>
            {
                if (t.IsFaulted) tcs.SetException(t.Exception.InnerException ?? t.Exception);
                else tcs.SetResult(true);
            }, System.Threading.Tasks.TaskScheduler.Default);
        }, PlayerLoopTiming.Update);
        return tcs.Task;
    }
}
