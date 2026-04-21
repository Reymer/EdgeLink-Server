using System;
using System.Collections.Generic;

/// <summary>
/// 測試用派發器：立即同步執行所有 Enqueue 的動作，不需要 MonoBehaviour。
/// </summary>
public class SyncDispatcher : IMainThreadDispatcher
{
    public List<Action> Invocations { get; } = new();

    public void Enqueue(Action action)
    {
        Invocations.Add(action);
        action?.Invoke();
    }
}
