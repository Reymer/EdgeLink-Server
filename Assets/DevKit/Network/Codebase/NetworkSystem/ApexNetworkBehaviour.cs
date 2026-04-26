using UnityEngine;
using VARLive.ApexNetwork;

public class ApexNetworkBehaviour : MonoBehaviour, IApexNetworkCallback, IChannelWorkCallback
{
    [SerializeField]
    private string ipAddress = "127.0.0.1";
    [SerializeField]
    private string appKey;
    [SerializeField]
    private bool autoConnectOnAwake = true;

    protected ApexNetworkSystem NetworkSystem { get; private set; } = new ApexNetworkSystem();

    public virtual void OnConnect()
    {
        Debug.Log("OnConnect");
    }

    public virtual void OnDisconnect()
    {
        Debug.Log("OnDisconnect");
    }

    public virtual void OnAddMember(User user)
    {
        Debug.Log("OnAddMember " + user.UserID);
    }

    public virtual void OnRemoveMember(User user)
    {
        Debug.Log("OnRemoveMember " + user.UserID);
    }

    public virtual void BindNetworkEvent(Channel channel)
    {
        Debug.Log(channel.Name + " BindNetworkEvent");
    }

    public virtual void OnCreateChannel(Channel channel)
    {
        Debug.Log(channel.Name + " OnCreateChannel");
    }

    private void Awake()
    {
        NetworkSystem.RegisterNetworkCallback(this);
        NetworkSystem.RegisterChannelWorkCallback(this);

        if (autoConnectOnAwake)
        {
            Connect(ipAddress, appKey);
        }
    }

    public void Connect(string ipAddress, string appKey)
    {
        NetworkSystem.Init(ipAddress, appKey);
        NetworkSystem.Connect();
    }

    private void OnDestroy()
    {
        NetworkSystem.UnregisterNetworkCallback(this);
        NetworkSystem.UnregisterChannelWorkCallback(this);
        NetworkSystem.Disconnect();
    }

    private void OnApplicationQuit()
    {
        NetworkSystem.Disconnect();
    }
}
