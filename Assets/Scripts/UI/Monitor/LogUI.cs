using TMPro;
using UnityEngine;

public class LogUI : MonoBehaviour
{
    [SerializeField] private TMP_Text logText;

    public void Log(string log)
    {
        if (logText != null)
            logText.text = log;
    }
}
