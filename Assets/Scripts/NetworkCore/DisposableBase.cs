using System;

/// <summary>
/// 統一資源釋放的抽象基底類
/// </summary>
public abstract class DisposableBase : IDisposable
{
    private bool disposed = false;
    private readonly object lockObj = new();

    public void Dispose()
    {
        lock (lockObj)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            DisposeManagedResources();
        }
    }

    /// <summary>
    /// 子類別必須實作：實際釋放邏輯
    /// </summary>
    protected abstract void DisposeManagedResources();
}
