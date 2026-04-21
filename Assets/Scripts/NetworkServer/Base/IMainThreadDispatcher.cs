using System;

/// <summary>
/// 主執行緒派發器介面，用於解耦 MonoBehaviour 依賴，使連接器可被單元測試。
/// </summary>
public interface IMainThreadDispatcher
{
    void Enqueue(Action action);
}
