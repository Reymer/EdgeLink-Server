using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;
using System.IO;

public class DeviceLockProcessor
{
    private CancellationTokenSource cancellationTokenSource;
    private SerialPort serialPort = new SerialPort();
    private readonly AESProcessor aesProcessor = new AESProcessor();
    private string message = string.Empty;
    private string lastMessage = string.Empty;
    private string applicationVariable = string.Empty;
    private string aesEncrypt = string.Empty;
    private string aesDecrypt = string.Empty;
    private float verificationTimer = 0f;
    private bool verificationFailed = false;


    public void Init()
    {
        try
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                if(serialPort!= null)
                {
                    serialPort = new SerialPort(port, 9600, Parity.None, 8, StopBits.One);
                }
            }

            if (serialPort != null)
            {
                serialPort.Open();
                cancellationTokenSource = new CancellationTokenSource();
                StartSendingRandomMessages(cancellationTokenSource.Token);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"初始化串口時發生錯誤：{ex.Message}");
        }
    }

    public void Update()
    {
        HandleVerification();
        HandleSerialPortCommunication();
    }

    private void HandleVerification()
    {
        if (!IsOpenSerialPort())
        {
            verificationFailed = true;
            applicationVariable = string.Empty;
        }
        else
        {
            applicationVariable = GetApplicationVariable();
            aesEncrypt = aesProcessor.EncryptWithAES(applicationVariable);
            aesDecrypt = aesProcessor.DecryptWithAES(aesEncrypt);

            if (string.IsNullOrEmpty(applicationVariable) || applicationVariable != aesDecrypt)
            {
                verificationFailed = true;
            }
            else
            {
                verificationFailed = false;
                verificationTimer = 0f;
            }
        }

        HandleVerificationFailure();
    }
    private void HandleVerificationFailure()
    {
        if (verificationFailed)
        {
            verificationTimer += Time.deltaTime;

            if (verificationTimer >= 5f)
            {
                Debug.LogError("驗證失敗超過 5 秒，退出應用程式");
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }
        else
        {
            verificationTimer = 0f;
        }
    }
    private void HandleSerialPortCommunication()
    {
        if (serialPort == null)
        {
            return;
        }

        try
        {
            if (!serialPort.IsOpen)
            {
                HandleHardwareRemoval();
                return;
            }

            if (serialPort.BytesToRead > 0)
            {
                string data = serialPort.ReadExisting()?.Trim();

                if (!string.IsNullOrEmpty(data))
                {
                    if (data.Length < 34)
                    {
                        return;
                    }

                    ProcessReceivedData(data, message);
                }
            }
        }
        catch (IOException ex)
        {
            Debug.LogError($"檢查到硬體鎖可能已拔掉！，IO異常：{ex.Message}");
            HandleHardwareRemoval();
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.LogError($"檢查到硬體鎖可能已拔掉！，訪問被拒絕：{ex.Message}");
            HandleHardwareRemoval();
        }
        catch (Exception ex)
        {
            Debug.LogError($"發生未知異常：{ex.Message}");
            HandleHardwareRemoval();
        }
    }

    private void HandleHardwareRemoval()
    {
        Debug.LogError("檢查到硬體鎖可能已拔掉，正在清理資源...");

        cancellationTokenSource?.Cancel();

        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                serialPort.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"關閉 Port 號時發生錯誤：{ex.Message}");
                return;
            }
        }

        serialPort = null;
        message = string.Empty;
        lastMessage = string.Empty;
    }
    private async void StartSendingRandomMessages(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                message = UnityEngine.Random.Range(0, 2) == 0 ? "1" : "2";
                SendData(message);
                await Task.Delay(10000, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
            Debug.Log("隨機訊息傳輸已停止。");
        }
        catch (Exception ex)
        {
            Debug.LogError($"傳輸隨機訊息時發生錯誤：{ex.Message}");
        }
    }

    public void SendData(string dataToSend)
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            this.message = dataToSend;
            serialPort.Write(dataToSend);
        }
    }

    private void ProcessReceivedData(string data, string message)
    {
        List<string> temp = new List<string>();
        StringBuilder currentString = new StringBuilder();
        bool isFinalMessage = false;
        int count33 = 0;

        for (int i = 0; i < data.Length - 1; i++)
        {
            if (i + 1 < data.Length && data[i] == '3' && data[i + 1] == '3')
            {
                count33++;
                i++;
            }

            if (count33 > 1)
            {
                return;
            }
        }

        for (int i = 0; i < data.Length; i++)
        {
            currentString.Append(data[i]);

            if (i + 1 < data.Length && data[i] == '3')
            {
                char nextChar = data[i + 1];

                if (nextChar == 'B' || nextChar == 'A' || nextChar == 'F' || nextChar == '3')
                {
                    temp.Add(currentString.ToString().Substring(0, currentString.Length - 1).Trim());
                    currentString.Clear();
                    i++;
                }
            }
            else if (i + 1 < data.Length && data[i] == '7' && data[i + 1] == 'C')
            {
                if (currentString.Length > 0)
                {
                    temp.Add(currentString.ToString().Substring(0, currentString.Length - 1).Trim());
                    currentString.Clear();
                }
                isFinalMessage = true;
                i++;
            }
            else if (isFinalMessage)
            {
                if (i + 1 < data.Length && (data[i] == '3' || data[i] == '7') &&
                    (data[i + 1] == 'B' || data[i + 1] == 'A' || data[i + 1] == 'F' || data[i + 1] == '3' || data[i + 1] == 'C'))
                {
                    temp.Add(currentString.ToString().Trim());
                    currentString.Clear();
                    isFinalMessage = false;
                }
            }
        }

        if (currentString.Length > 0)
        {
            temp.Add(currentString.ToString().Trim());
        }

        if (temp.Count != 6)
        {
            return;
        }

        foreach (var item in temp)
        {

            if (item.Length > 0 && item[0] == 'F' && message.Equals("1"))
            {
                return;
            }

            if (item.Length > 0 && item[0] == '5' && message.Equals("2"))
            {
                return;
            }
        }

        int baseValue = 0;
        bool isBaseValueSet = false;
        StringBuilder resultString = new StringBuilder();

        for (int i = 0; i < temp.Count; i++)
        {
            string hexValue = temp[i].Trim();
            if (!string.IsNullOrEmpty(hexValue) && IsValidHex(hexValue))
            {
                try
                {
                    int decimalValue = Convert.ToInt32(hexValue, 16);
                    if (!isBaseValueSet)
                    {
                        baseValue = decimalValue;
                        isBaseValueSet = true;
                    }
                    else
                    {
                        int result = message == "1" ? decimalValue - baseValue : decimalValue + baseValue;
                        List<int> splitParts = SplitNumber(result.ToString());
                        foreach (var part in splitParts)
                        {
                            if (part >= 0 && part <= 255)
                            {
                                resultString.Append((char)part);
                                lastMessage = resultString.ToString();
                            }
                            else
                            {
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"轉換資料時發生錯誤：{ex.Message}");
                    return;
                }
            }
        }
    }


    private List<int> SplitNumber(string number)
    {
        List<int> parts = new List<int>();
        int index = 0;

        while (index < number.Length)
        {
            int remainingLength = number.Length - index;

            if (remainingLength == 5)
            {
                if (number[index] == '8' || number[index] == '7')
                {
                    parts.Add(int.Parse(number.Substring(index, 2)));
                    index += 2;
                    parts.Add(int.Parse(number.Substring(index, 3))); 
                    index += 3;
                }
                else
                {
                    parts.Add(int.Parse(number.Substring(index, 3)));
                    index += 3;
                    parts.Add(int.Parse(number.Substring(index, 2)));
                    index += 2;
                }
            }
            else if (remainingLength == 6)
            {
                parts.Add(int.Parse(number.Substring(index, 3)));
                index += 3;
                parts.Add(int.Parse(number.Substring(index, 3)));
                index += 3;
            }
            else if (remainingLength == 4)
            {
                parts.Add(int.Parse(number.Substring(index, 2)));
                index += 2;
                parts.Add(int.Parse(number.Substring(index, 2)));
                index += 2;
            }
            else
            {
                break;
            }
        }
        return parts;
    }

    public bool IsOpenSerialPort()
    {
        return serialPort != null && serialPort.IsOpen;
    }

    private bool IsValidHex(string value)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(value, @"\A\b[0-9a-fA-F]+\b\Z");
    }

    public string GetApplicationVariable()
    {
        return lastMessage;
    }

    public void UnInit()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                serialPort.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"關閉 Port 號時發生錯誤：{ex.Message}");
            }
        }
    }
}