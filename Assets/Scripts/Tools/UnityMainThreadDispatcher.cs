using System;
using System.Collections.Generic;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher instance = null;
    private static readonly Queue<Action> executionQueue = new();
    private static bool isQuitting = false;

    /// <summary>
    /// 獲取 UnityMainThreadDispatcher 實例
    /// </summary>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static UnityMainThreadDispatcher Instance()
    {
        if (isQuitting)
        {
            Debug.LogWarning("應用程式已退出後嘗試存取 UnityMainThreadDispatcher。");
            return null;
        }

        if (!instance)
        {
            throw new Exception("UnityMainThreadDispatcher 尚未初始化。請將其新增到場景中的一個 GameObject。");
        }
        return instance;
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    public void Update()
    {
        lock (executionQueue)
        {
            while (executionQueue.Count > 0)
            {
                executionQueue.Dequeue().Invoke();
            }
        }
    }

    /// <summary>
    /// 將 Action 加入執行佇列
    /// </summary>
    /// <param name="action"></param>
    public void Enqueue(Action action)
    {
        lock (executionQueue)
        {
            executionQueue.Enqueue(action);
        }
    }

    private void OnDestroy()
    {
        isQuitting = true;
    }
}
