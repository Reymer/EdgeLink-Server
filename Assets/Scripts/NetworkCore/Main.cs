using UnityEngine;

public class Main : MonoBehaviour
{
    private NetworkPortTableUIManager networkPortTableUIManager;
    private void Awake()
    {
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
}
