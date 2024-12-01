using UnityEngine;
using VARLive.ApexNetwork;
using DevKit.Console;

public class Main : MonoBehaviour, IApexNetworkCallback
{
    private NetworkPortTableUIManager networkPortTableUIManager;
    private void Awake()
    {
        NetworkService.RegisterNetworkCallback(this);
        NetworkService.Connect("127.0.0.1", "IotServer");
        NetworkPortManager.Instance.Init();
    }

    private void Start()
    {
        networkPortTableUIManager = GameObject.FindObjectOfType<NetworkPortTableUIManager>();
        networkPortTableUIManager.Init();
    }

    private void OnApplicationQuit()
    {
        NetworkPortManager.Instance.DeInit();
        networkPortTableUIManager.DeInit();
    }

    public void OnConnect()
    {
        ULog.Log("已連線");
    }

    public void OnDisconnect()
    {
        ULog.Log("已斷線");
    }

    public void OnAddMember(User user)
    {
        Debug.Log($"使用者連線。使用者位址: {user.Address}, 使用者ID: {user.UserID}, 使用者名稱: {user.MachineName}");
    }

    public void OnRemoveMember(User user)
    {
        Debug.Log($"使用者斷線。使用者位址: {user.Address}, 使用者ID: {user.UserID}, 使用者名稱: {user.MachineName}");
    }
}
