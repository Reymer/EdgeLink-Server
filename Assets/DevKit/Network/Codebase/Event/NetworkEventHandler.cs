using DevKit;
using System;
using System.Collections;
using UnityEngine;
using VARLive.ApexNetwork;

public class NetworkEventHandler : IEventHandler
{
    private Channel channel;
    private bool isUseUdp;

    public NetworkEventHandler(Channel channel, bool isUseUdp)
    {
        this.channel = channel;
        this.isUseUdp = isUseUdp;
    }

    public NetworkEventHandler(Channel channel)
    {
        this.channel = channel;
        isUseUdp = false;
    }

    public void BindAll(Action<string, object[]> listener)
    {
        channel.BindAllInMainThread(listener);
    }
    public void Bind(string eventName, Action action)
    {
        channel.BindInMainThread(eventName, action);
    }

    public void Bind<T>(string eventName, Action<T> action)
    {
        channel.BindInMainThread(eventName, action);
    }

    public void Bind<T1, T2>(string eventName, Action<T1, T2> action)
    {
        channel.BindInMainThread(eventName, action);
    }

    public void Bind<T1, T2, T3>(string eventName, Action<T1, T2, T3> action)
    {
        channel.BindInMainThread(eventName, action);
    }

    public void Bind<T1, T2, T3, T4>(string eventName, Action<T1, T2, T3, T4> action)
    {
        channel.BindInMainThread(eventName, action);
    }

    public void Bind<T1, T2, T3, T4, T5>(string eventName, Action<T1, T2, T3, T4, T5> action)
    {
        channel.BindInMainThread(eventName, action);
    }

    public void Bind<T1, T2, T3, T4, T5, T6>(string eventName, Action<T1, T2, T3, T4, T5, T6> action)
    {
        channel.BindInMainThread(eventName, action);
    }

    public void TriggerUdp(string eventName, params object[] datas)
    {
        channel.TriggerUdp(eventName, datas);
    }

    public void Trigger(string eventName, params object[] datas)
    {
        if (isUseUdp)
        {
            channel.TriggerUdp(eventName, datas);
        }
        else
        {
#if NetworkLog
            Debug.Log($"Network trigger {eventName}");
#endif
            channel.Trigger(eventName, datas);
        }
    }

    public void Unbind(string eventName, Action action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T>(string eventName, Action<T> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T1, T2>(string eventName, Action<T1, T2> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T1, T2, T3>(string eventName, Action<T1, T2, T3> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T1, T2, T3, T4>(string eventName, Action<T1, T2, T3, T4> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T1, T2, T3, T4, T5>(string eventName, Action<T1, T2, T3, T4, T5> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }

    public void Unbind<T1, T2, T3, T4, T5, T6>(string eventName, Action<T1, T2, T3, T4, T5, T6> action)
    {
        channel.UnbindInMainThread(eventName, action);
    }
}
