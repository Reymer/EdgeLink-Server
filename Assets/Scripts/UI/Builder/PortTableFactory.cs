using UnityEngine;

public class PortTableFactory : MonoBehaviour
{
    [SerializeField] private GameObject prefab;

    public GameObject CreateTable(Transform parent)
    {
        if (prefab == null || parent == null)
        {
            Debug.LogError("PortTableFactory: prefab 或 parent 為 null");
            return null;
        }

        GameObject instance = Instantiate(prefab, parent);
        instance.SetActive(true);
        return instance;
    }
}
