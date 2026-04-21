using System;
using Cysharp.Threading.Tasks;

/// <summary>
/// 生產環境用：把 IMainThreadDispatcher 呼叫橋接到 UniTask PlayerLoop，不依賴 MonoBehaviour。
/// </summary>
public class UnityDispatcherAdapter : IMainThreadDispatcher
{
    public void Enqueue(Action action)
    {
        UniTask.Post(action, PlayerLoopTiming.Update);
    }
}
