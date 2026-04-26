# EdgeLink SDK 使用說明

EdgeLink SDK 讓 Unity 遊戲或應用程式透過 TCP / UDP 與 EdgeLink Server 通訊，接收 IoT 設備資料並可雙向發送指令。

---

## 架構概覽

```
IoT 設備  ──→  EdgeLink Server  ──→  你的 Unity 應用
                                       (EdgeLinkReceiver)
```

EdgeLink Server 負責管理 IoT 設備連線與遮罩處理；EdgeLinkReceiver 負責接收處理後的資料，並可透過 EdgeLink Server 將指令反向送回 IoT 設備。

---

## 安裝

1. 將 `EdgeLinkSdk.dll` 放入 `Assets/Plugins/`
2. 如需遮罩解析，將 MaskEditor 匯出的 `.json` 放入 `Assets/StreamingAssets/`
3. 在 EdgeLink Server Web UI 新增一個 **TCP Client**，設定目標 IP 為此機器 IP，Port 對應到 `listenPort`

---

## 快速開始

```csharp
var receiver = new EdgeLinkReceiver(Protocol.TCP);

receiver.OnMessage += msg => Debug.Log(msg.Raw);

receiver.Start(9090); // 監聽 port 9090
```

在 `MonoBehaviour.Update()` 中呼叫 `Flush()`，事件才會在 Unity 主執行緒觸發：

```csharp
void Update() => receiver.Flush();
```

---

## EdgeLinkReceiver

### 建構子

```csharp
new EdgeLinkReceiver(Protocol protocol, MaskDefinition definition = null)
```

| 參數 | 說明 |
|---|---|
| `protocol` | `Protocol.TCP` 或 `Protocol.UDP` |
| `definition` | 遮罩定義（選填）。傳入後 `IotMessage.Parsed` 才有值 |

### 方法

| 方法 | 說明 |
|---|---|
| `Start(int port)` | 開始監聽指定 port |
| `Stop()` | 停止監聽，釋放所有連線 |
| `Flush()` | 在 `Update()` 呼叫，讓事件在主執行緒觸發 |
| `SendAsync(string message)` | 發送訊息給 EdgeLink Server（TCP 限定，用於雙向通訊） |
| `Dispose()` | 等同 `Stop()` |

### 事件

| 事件 | 簽章 | 說明 |
|---|---|---|
| `OnMessage` | `Action<IotMessage>` | 每筆資料到達時觸發 |
| `OnConnectionChanged` | `Action<bool>` | EdgeLink Server 連線/斷線時觸發 |
| `OnDeviceStatusChanged` | `Action<string, bool>` | IoT 設備連線/斷線時觸發，`string` 為 Protocol Name |
| `OnError` | `Action<Exception>` | 發生錯誤時觸發 |

### 屬性

| 屬性 | 說明 |
|---|---|
| `LatestMessage` | 最後一筆收到的訊息，可直接在 `Update()` 讀取 |
| `IsListening` | 目前是否正在監聽 |
| `Protocol` | 使用的通訊協定 |

---

## IotMessage

每次 `OnMessage` 觸發時收到的訊息物件。

| 屬性 | 型別 | 說明 |
|---|---|---|
| `Raw` | `string` | EdgeLink 遮罩處理後的原始字串，例如 `"ID=2;TEMP=25"` |
| `Parsed` | `ParseResult` | 遮罩解析結果，建構子未傳入 `MaskDefinition` 時為 `null` |

---

## ParseResult

建構 `EdgeLinkReceiver` 時傳入 `MaskDefinition`，`IotMessage.Parsed` 才有值。

| 屬性 | 型別 | 說明 |
|---|---|---|
| `Fields` | `Dictionary<string, string>` | 從原始資料解析出的欄位值 |
| `Output` | `string` | 遮罩輸出字串 |
| `Success` | `bool` | 解析是否成功 |
| `Error` | `string` | 失敗原因（`Success` 為 `false` 時才有值） |

---

## 遮罩定義（MaskDefinition）

從 MaskEditor 匯出的 `.json` 檔載入：

```csharp
var mask = MaskDefinition.FromJsonFile(path);
var receiver = new EdgeLinkReceiver(Protocol.TCP, mask);
```

或從字串載入：

```csharp
var mask = MaskDefinition.FromJson(jsonString);
```

---

## MaskParser

如果需要在 `OnMessage` 以外的地方單獨解析字串，可直接使用 `MaskParser`：

```csharp
var parser = new MaskParser(mask);

// 從 EdgeLink 輸出字串反向提取欄位值
// 例如 outputTemplate = "ID={ID};TEMP={TEMP}"
// 輸入 "ID=2;TEMP=25" → Fields["ID"]="2", Fields["TEMP"]="25"
var fields = parser.ParseOutput("ID=2;TEMP=25");

// 解析原始文字訊息
ParseResult result = parser.Parse("ID:2;TEMP:25");

// 解析二進位資料
ParseResult result = parser.ParseBinary(bytes);
```

---

## 事件說明

### OnConnectionChanged vs OnDeviceStatusChanged

這兩個事件代表不同層的連線狀態：

```
IoT 設備 ──連線──→ EdgeLink Server ──連線──→ EdgeLinkReceiver
              ↑                                      ↑
    OnDeviceStatusChanged                  OnConnectionChanged
    （IoT 設備上/離線）                   （EdgeLink Server 連/斷線）
```

- **`OnConnectionChanged`**：EdgeLink Server 本身與你的應用連線變化。EdgeLink Server 掛掉或網路中斷時觸發。
- **`OnDeviceStatusChanged`**：某個 IoT 設備與 EdgeLink Server 之間的連線變化。設備斷電或重新上線時觸發，EdgeLink Server 本身仍在線。

### 為什麼 OnDeviceStatusChanged 有 portName？

EdgeLink Server 可同時管理多個 IoT 設備，每個設備對應一個 Protocol Name。`portName` 讓你知道是哪個設備的狀態改變。

---

## 雙向通訊

`SendAsync` 適用於 EdgeLink Server 設定為雙向通訊的場景（Serial / Polling / Concurrent 模式），可將指令透過 EdgeLink Server 轉發給 IoT 設備。

```csharp
// 發送指令
await receiver.SendAsync("CMD=RESET");

// 搭配 UI Button（async void 適用於事件處理）
public async void OnResetButtonClick()
{
    await receiver.SendAsync("CMD=RESET");
}
```

> **注意**：`SendAsync` 僅支援 TCP，UDP 模式呼叫無效。

---

## 完整範例

```csharp
using EdgeLink;
using UnityEngine;

public class MyIoTManager : MonoBehaviour
{
    private EdgeLinkReceiver _receiver;

    void Start()
    {
        // 載入遮罩定義（選填）
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "sensor.json");
        MaskDefinition mask = System.IO.File.Exists(path)
            ? MaskDefinition.FromJsonFile(path)
            : null;

        _receiver = new EdgeLinkReceiver(Protocol.TCP, mask);

        // EdgeLink Server 連線狀態
        _receiver.OnConnectionChanged += connected =>
            Debug.Log($"EdgeLink Server {(connected ? "已連線" : "已斷線")}");

        // IoT 設備連線狀態
        _receiver.OnDeviceStatusChanged += (portName, connected) =>
            Debug.Log($"設備 [{portName}] {(connected ? "上線" : "離線")}");

        // 接收資料
        _receiver.OnMessage += msg =>
        {
            Debug.Log($"收到：{msg.Raw}");

            if (msg.Parsed != null && msg.Parsed.Success)
            {
                if (msg.Parsed.Fields.TryGetValue("TEMP", out string temp))
                    Debug.Log($"溫度：{temp}");
            }
        };

        _receiver.OnError += ex => Debug.LogError($"錯誤：{ex.Message}");

        _receiver.Start(9090);
    }

    void Update() => _receiver?.Flush();

    void OnDestroy() => _receiver?.Dispose();

    // 發送指令給 IoT 設備
    public async void SendReset() => await _receiver.SendAsync("CMD=RESET");
}
```

---

## 常見問題

**Q：為什麼事件沒有觸發？**
確認 `Flush()` 有在 `Update()` 中呼叫。事件是在背景執行緒收到後排入佇列，`Flush()` 才會在主執行緒觸發。

**Q：`IotMessage.Parsed` 永遠是 null？**
建構 `EdgeLinkReceiver` 時需傳入 `MaskDefinition`，否則不會解析。

**Q：UDP 可以用 SendAsync 嗎？**
不行。UDP 是無連線協定，`SendAsync` 僅支援 TCP。如需 UDP 雙向通訊，需自行建立 `UdpClient` 發送端。

**Q：可以同時有多個 EdgeLink Server 連入嗎？**
可以。`EdgeLinkReceiver` 是 TCP Server，支援多條連線同時存在，`SendAsync` 會廣播給所有連線。
