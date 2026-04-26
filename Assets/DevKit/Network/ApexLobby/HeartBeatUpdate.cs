using DevKit;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class HeartBeatUpdate : IUpdate
{
    public bool IsAlive { get; private set; }

    public const float DefaultInterval = 3;
    public const float DefaultTimeout = 30;

    private const string HEARTBEAT = "HeartBeat";
    private const string HEARTBEAT_UDP = "HeartBeatUdp";
    private const string HEARTBEAT_RESPONSE = "HeartBeatResponse";
    private const string HEARTBEAT_RESPONSE_UDP = "HeartBeatResponseUdp";

    private UnityTimer timer;
    private UnityTimer tcpTimeoutTimer;
    private UnityTimer udpTimeoutTimer;
    private INetworkIdentity networkIdentity;
    private IEventHandler eventHandler;
    
    public HeartBeatUpdate(float interval, float timeout)
    {
        timer = new UnityTimer(interval, TimeDirection.Positive);
        tcpTimeoutTimer = new UnityTimer(timeout, TimeDirection.Positive);
        udpTimeoutTimer = new UnityTimer(timeout, TimeDirection.Positive);
    }

    public void SetupNetwork(INetworkIdentity identity, IEventHandler eventHandler)
    {
        this.networkIdentity = identity;
        this.eventHandler = eventHandler;
    }

    public void BindNetworkEvent()
    {
        if (networkIdentity.IsLocal())
        {
            eventHandler.Bind<int>(HEARTBEAT, OnHeartBeat);
            eventHandler.Bind<int>(HEARTBEAT_UDP, OnHeartBeatWithUpd);
        }
        eventHandler.Bind<int>(HEARTBEAT_RESPONSE, OnHeartBeatResponse);
        eventHandler.Bind<int>(HEARTBEAT_RESPONSE_UDP, OnHeartBeatResponseWithUpd);
    }

    public void UnbindNetworkEvent()
    {
        if (networkIdentity.IsLocal())
        {
            eventHandler.Unbind<int>(HEARTBEAT, OnHeartBeat);
            eventHandler.Unbind<int>(HEARTBEAT_UDP, OnHeartBeatWithUpd);
        }
        eventHandler.Unbind<int>(HEARTBEAT_RESPONSE, OnHeartBeatResponse);
        eventHandler.Unbind<int>(HEARTBEAT_RESPONSE_UDP, OnHeartBeatResponseWithUpd);
    }

    private void OnHeartBeatResponseWithUpd(int userID)
    {
        if (networkIdentity.GetNetworkID() == userID)
        {
            Debug.Log($"My ID:{0} is Alive On UDP");

            IsAlive = true;
            udpTimeoutTimer.Reset();
        }
    }

    private void OnHeartBeatWithUpd(int userID)
    {
        var tcpEventHandler = eventHandler as NetworkEventHandler;
        if (tcpEventHandler != null)
        {
            tcpEventHandler.TriggerUdp(HEARTBEAT_RESPONSE_UDP, userID);
        }
    }

    private void OnHeartBeatResponse(int userID)
    {
        if (networkIdentity.GetNetworkID() == userID)
        {
            Debug.Log($"My ID:{0} is Alive On TCP");

            IsAlive = true;
            tcpTimeoutTimer.Reset();
        }
    }

    private void OnHeartBeat(int userID)
    {
        eventHandler.Trigger(HEARTBEAT_RESPONSE, userID);
    }

    public void OnUpdate()
    {
        if (eventHandler == null)
        {
            return;
        }

        if (timer.IsTickOrTimeUp(Time.deltaTime))
        {
            eventHandler.Trigger(HEARTBEAT, networkIdentity.GetNetworkID());
            var udpEventHandler = eventHandler as NetworkEventHandler;
            if (udpEventHandler != null)
            {
                udpEventHandler.TriggerUdp(HEARTBEAT_UDP, networkIdentity.GetNetworkID());
            }
        }

        if (tcpTimeoutTimer.IsTickOrTimeUp(Time.deltaTime))
        {
            Debug.Log($"My ID:{0} is Dead On TCP");

            IsAlive = false;
            tcpTimeoutTimer.Reset();
        }

        if (udpTimeoutTimer.IsTickOrTimeUp(Time.deltaTime))
        {
            Debug.Log($"My ID:{0} is Dead On UDP");

            IsAlive = false;
            udpTimeoutTimer.Reset();
        }
    }
}
