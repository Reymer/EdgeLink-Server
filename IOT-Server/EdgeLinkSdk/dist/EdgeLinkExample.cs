using UnityEngine;
using UnityEngine.UI;
using System.IO;
using EdgeLink;

/// <summary>
/// EdgeLink SDK 使用範例。
/// 將此腳本掛在任一 GameObject 上。
/// 需要在 Package Manager 安裝 com.unity.nuget.newtonsoft-json。
/// </summary>
public class EdgeLinkExample : MonoBehaviour
{
    [Header("通訊協定")]
    [Tooltip("選擇 TCP 或 UDP")]
    public Protocol protocol = Protocol.TCP;

    [Header("連線設定")]
    [Tooltip("IoT Server 的目標 Port 要與這裡一致")]
    public int listenPort = 9090;

    [Tooltip("韌體工程師提供的遮罩定義 JSON，放在 Assets/StreamingAssets/ 資料夾")]
    public string maskFileName = "sensor.json";

    [Header("UI（選填，可不掛）")]
    public Text statusText;
    public Text rawText;
    public Text fieldText;

    // ── 內部 ──────────────────────────────────────────────────────────────────

    private EdgeLinkReceiver receiver;
    private MaskParser parser;

    // ── Unity 生命週期 ────────────────────────────────────────────────────────

    void Start()
    {
        // 載入遮罩定義（韌體工程師提供的 JSON）
        MaskDefinition definition = LoadDefinition(maskFileName);

        // 若有定義，建立 parser 供後續提取欄位用
        if (definition != null)
            parser = new MaskParser(definition);

        // 建立接收器
        receiver = new EdgeLinkReceiver(protocol, definition);

        receiver.OnConnectionChanged += OnConnectionChanged;
        receiver.OnMessage           += OnMessage;
        receiver.OnError             += OnError;

        receiver.Start(listenPort);
        Debug.Log($"[EdgeLink] 開始監聽 {protocol} port {listenPort}");
    }

    void Update()
    {
        // 必須呼叫，事件才會在主執行緒觸發
        receiver?.Flush();

        // ── Polling 方式（不用事件也可以）────────────────────────────────────
        // var latest = receiver.LatestMessage;
        // if (latest != null)
        //     rawText.text = latest.Raw;
    }

    void OnDestroy()
    {
        receiver?.Dispose();
    }

    // ── 事件回呼 ──────────────────────────────────────────────────────────────

    void OnConnectionChanged(bool connected)
    {
        string status = connected ? "已連線" : "已斷線";
        Debug.Log($"[EdgeLink] {status}");
        if (statusText != null) statusText.text = status;
    }

    void OnMessage(IotMessage msg)
    {
        // msg.Raw = IoT Server 遮罩處理後的完整輸出字串，例如 "ID=2;TEMP=5"
        Debug.Log($"[EdgeLink] 收到：{msg.Raw}");
        if (rawText != null) rawText.text = msg.Raw;

        // 從輸出字串反向提取欄位值
        if (parser != null)
        {
            var fields = parser.ParseOutput(msg.Raw);

            // 取單一欄位
            if (fields.TryGetValue("ID",   out string id))   Debug.Log($"ID = {id}");
            if (fields.TryGetValue("TEMP", out string temp)) Debug.Log($"TEMP = {temp}");

            // 顯示所有欄位
            if (fieldText != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in fields)
                    sb.AppendLine($"{kv.Key} = {kv.Value}");
                fieldText.text = sb.ToString();
            }
        }
    }

    void OnError(System.Exception ex)
    {
        Debug.LogError($"[EdgeLink] 錯誤：{ex.Message}");
    }

    // ── 工具方法 ──────────────────────────────────────────────────────────────

    private MaskDefinition LoadDefinition(string fileName)
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[EdgeLink] 找不到遮罩定義：{path}，將只接收原始字串。");
            return null;
        }

        try
        {
            var def = Newtonsoft.Json.JsonConvert.DeserializeObject<MaskDefinition>(
                File.ReadAllText(path));
            Debug.Log($"[EdgeLink] 遮罩定義載入成功：{def.maskId}");
            return def;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[EdgeLink] 遮罩定義載入失敗：{ex.Message}");
            return null;
        }
    }
}
