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
    public Protocol protocol  = Protocol.TCP;
    public int      listenPort = 9090;

    [Header("遮罩定義（選填）")]
    [Tooltip("MaskEditor 匯出的 .json，放在 Assets/StreamingAssets/")]
    public string maskFile = "sensor.json";

    [Header("UI（可不掛）")]
    public Text statusText;
    public Text rawText;
    public Text fieldsText;

    private EdgeLinkReceiver _receiver;
    private MaskParser       _parser;

    // ── 生命週期 ──────────────────────────────────────────────────────────────

    void Start()
    {
        MaskDefinition mask = TryLoadMask(maskFile);
        if (mask != null)
            _parser = new MaskParser(mask);

        _receiver = new EdgeLinkReceiver(protocol, mask);
        _receiver.OnConnectionChanged += OnConnected;
        _receiver.OnMessage           += OnMessage;
        _receiver.OnError             += OnError;
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
        Debug.Log($"[EdgeLink] {s}");
        if (statusText) statusText.text = s;
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
