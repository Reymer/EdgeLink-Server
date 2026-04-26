using UnityEngine;
using UnityEngine.UI;

public class LogUI : MonoBehaviour
{
    [SerializeField] private Text logText;

    public void Log(string log)
    {
        if (logText != null)
            logText.text = log;
    }
}
