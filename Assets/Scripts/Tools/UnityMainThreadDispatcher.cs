using System;
using System.Collections.Generic;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher instance = null;
    private static readonly Queue<Action> executionQueue = new();
    private static bool isQuitting = false;

    public static UnityMainThreadDispatcher Instance()
    {
        if (isQuitting)
        {
            Debug.LogWarning("UnityMainThreadDispatcher is being accessed after the application has quit.");
            return null;
        }

        if (!instance)
        {
            throw new Exception("UnityMainThreadDispatcher is not initialized. Please add it to a GameObject in the scene.");
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
