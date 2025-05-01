using UnityEngine;

public class Main : MonoBehaviour
{
    [SerializeField] private NetworkPortTableUIManager networkPortTableUIManager;
    private void Awake()
    {
        NetworkPortManager.Instance.Init();
    }

    private void Start()
    {
        Init();
    }

    private void Init()
    {
        networkPortTableUIManager.Init();
    }

    private void OnApplicationQuit()
    {
        NetworkPortManager.Instance.UnInit();
        networkPortTableUIManager.UnInit();
    }
}
