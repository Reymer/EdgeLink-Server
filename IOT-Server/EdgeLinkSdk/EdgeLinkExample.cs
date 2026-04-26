using System.IO;
using System.Text;
using EdgeLink;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EdgeLink SDK 使用範例。
/// 將此腳本掛在任一 GameObject 上即可運作。
///
/// 安裝步驟：
///   1. 將 EdgeLinkSdk.dll 和同資料夾內所有 .dll 放進 Assets/Plugins/
///   2. 將 MaskEditor 匯出的 sensor.json 放進 Assets/StreamingAssets/
///   3. 在 IoT Server Web UI 新增一個 TCP Client，目標 IP = 此機器 IP，Port = listenPort
/// </summary>
public class EdgeLinkExample : MonoBehaviour
{
    [Header("通訊設定")]
    public Protocol protocol   = Protocol.TCP;
    public int      listenPort = 9090;

    [Header("遮罩定義（選填）")]
    [Tooltip("MaskEditor 匯出的 .json，放在 Assets/StreamingAssets/")]
    public string maskFile = "sensor.json";

    [Header("UI（可不掛）")]
    public Text statusText;
    public Text rawText;
    public Text fieldsText;
    public Text deviceStatusText;

    private EdgeLinkReceiver _receiver;
    private MaskParser       _parser;

    // ── 生命週期 ──────────────────────────────────────────────────────────────

    void Start()
    {
        MaskDefinition mask = TryLoadMask(maskFile);
        if (mask != null)
            _parser = new MaskParser(mask);

        _receiver = new EdgeLinkReceiver(protocol, mask);
        _receiver.OnConnectionChanged  += OnConnected;
        _receiver.OnDeviceStatusChanged += OnDeviceStatus;
        _receiver.OnMessage            += OnMessage;
        _receiver.OnError              += OnError;
        _receiver.Start(listenPort);

        Debug.Log($"[EdgeLink] 監聽 {protocol} port {listenPort}");
    }

    void Update()
    {
        // 必須在 Update() 呼叫，事件才能在 Unity 主執行緒觸發
        _receiver?.Flush();
    }

    void OnDestroy() => _receiver?.Dispose();

    // ── 事件 ──────────────────────────────────────────────────────────────────

    void OnConnected(bool connected)
    {
        string s = connected ? "已連線" : "已斷線";
        Debug.Log($"[EdgeLink] EdgeLink Server {s}");
        if (statusText) statusText.text = s;
    }

    void OnDeviceStatus(string portName, string endpoint, bool connected)
    {
        // IoT 設備本身的連線狀態（與 EdgeLink Server 的連線無關）
        // endpoint 為設備的 IP:Port，例如 "192.168.1.101:5000"
        // 同一個 portName 下有多台設備時，可透過 endpoint 區分個別設備
        string s = connected ? "上線" : "離線";
        Debug.Log($"[EdgeLink] 設備 [{portName}] ({endpoint}) {s}");
        if (deviceStatusText) deviceStatusText.text = $"{portName} ({endpoint}): {s}";
    }

    void OnMessage(IotMessage msg)
    {
        // msg.Raw = IoT Server 遮罩輸出後的完整字串，例如 "ID=2;TEMP=25"
        Debug.Log($"[EdgeLink] {msg.Raw}");
        if (rawText) rawText.text = msg.Raw;

        if (_parser == null) return;

        // 從輸出字串反向提取各欄位值
        var fields = _parser.ParseOutput(msg.Raw);

        if (fields.TryGetValue("ID",   out string id))   Debug.Log($"ID   = {id}");
        if (fields.TryGetValue("TEMP", out string temp)) Debug.Log($"TEMP = {temp}");

        if (fieldsText)
        {
            var sb = new StringBuilder();
            foreach (var kv in fields)
                sb.AppendLine($"{kv.Key} = {kv.Value}");
            fieldsText.text = sb.ToString();
        }
    }

    void OnError(System.Exception ex) =>
        Debug.LogError($"[EdgeLink] 錯誤：{ex.Message}");

    // ── 發送指令範例 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 對 IoT 設備發送指令（透過 EdgeLink Server 轉發）。
    /// 可綁定在 UI Button 的 OnClick 上呼叫。
    /// </summary>
    public async void SendCommand(string command)
    {
        if (_receiver == null) return;
        await _receiver.SendAsync(command);
        Debug.Log($"[EdgeLink] 已送出指令：{command}");
    }

    // ── 工具 ──────────────────────────────────────────────────────────────────

    private static MaskDefinition TryLoadMask(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;

        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[EdgeLink] 找不到 {path}，將只接收原始字串。");
            return null;
        }

        try
        {
            var def = MaskDefinition.FromJsonFile(path);
            Debug.Log($"[EdgeLink] 遮罩載入：{def.maskId}");
            return def;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[EdgeLink] 遮罩載入失敗：{ex.Message}");
            return null;
        }
    }
}
