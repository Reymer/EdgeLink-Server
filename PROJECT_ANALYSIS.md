# EdgeLink Server (iot-server) 專案分析

> 作者：昌霖 · 初版 2024/09/27 · 文件更新 2026/04/23
> 分支：`develop`

---

## 1. 專案定位

**EdgeLink Server** 是一套以 **Unity** 為殼的 **TCP/UDP 封包中繼 / 轉發伺服器**，用途是讓硬體（感測器、嵌入式裝置）與軟體（Unity、桌面/Web 應用）之間的資料串接更簡單直觀。

核心能力：

- 同時管理多組 **TCP Server / TCP Client / UDP** 連線
- 依「**遮罩定義 (Mask Definition)**」解析收到的文字 / 二進位封包
- 將解析後的欄位以 **輸出模板 (outputTemplate)** 重組後再轉發到目的端
- 提供 **Web UI (port 8181)** 做連線表管理、監控、遮罩編輯與日誌
- 提供 **EdgeLinkSdk** 讓下游的 Unity/.NET 應用直接收取已處理好的訊息

適合用於：IoT 原型開發、感測器資料前處理、多硬體串接測試平台。

---

## 2. 專案根目錄結構

```
iot-server/
├── Assets/                       # Unity 專案主體 (C# Scripts, Scenes, Plugins)
│   ├── Scripts/
│   │   ├── NetworkServer/        # TCP/UDP 核心 + 路由 + 服務層
│   │   ├── Mask/                 # Unity 端遮罩處理
│   │   ├── UI/                   # Unity 介面 (Port 表格、Monitor、Console)
│   │   ├── WebApi/               # HttpListener + REST 路由 + SSE
│   │   └── Tools/                # MainThreadDispatcher, JSON 工具
│   ├── DevKit/ · Editor/ · Extra/ · GameData/ · GameResources/
│   ├── Plugins/ · Scenes/ · Tests/
│   └── portData.json             # 預設連線埠資料
├── IOT-Server/
│   ├── EdgeLinkSdk/              # 獨立 .NET SDK (給外部專案 import)
│   ├── EdgeLinkSdk.Tests/        # xUnit 單元測試
│   └── WebUI/                    # index.html + MaskEditor.html (server 直送)
├── MaskEditorApp/                # Python + pywebview 離線版遮罩編輯器
├── Setting/                      # 執行期設定檔 (Port、Mask、Retry)
├── GameData/ · Excel/            # 多語系、表格資料
├── docs/                         # 使用說明 PDF (zh-TW / en-US)
├── Packages/ · ProjectSettings/  # Unity 套件與專案設定
├── BuildSetting.json             # 自訂建置流程設定
├── gen_app_icon.py · gen_manual.py
├── iot-server.sln                # Visual Studio 解決方案
└── README.md
```

---

## 3. 系統架構

```
┌──────────────────────────────────────────────────────────────────────────┐
│                      EdgeLink Server (Unity App)                         │
│                                                                          │
│  ┌──────────────┐    ┌──────────────────┐    ┌────────────────────────┐ │
│  │ Main.cs      │───▶│ NetworkPortMgr   │───▶│ NetworkConnectorCore   │ │
│  │ (啟動入口)   │    │ (註冊/追蹤 Port) │    │ (TCP Server/Client/UDP)│ │
│  └──────┬───────┘    └──────────────────┘    └───────────┬────────────┘ │
│         │                                                 │              │
│         ▼                                                 ▼              │
│  ┌──────────────┐                             ┌──────────────────────┐   │
│  │ HttpApiServer│                             │ NetworkMessageRouter │   │
│  │ (port 8181)  │                             │ + MaskProcessor      │   │
│  └──────┬───────┘                             └──────────┬───────────┘   │
│         │   REST / SSE                                   │ Apply Mask    │
│         ▼                                                ▼               │
│  ┌─────────────────────────────┐          ┌─────────────────────────┐    │
│  │  WebUI (index.html)         │          │ 轉發到 TCP Client 目標  │    │
│  │  MaskEditor (HTML 版)       │          └─────────────────────────┘    │
│  └─────────────────────────────┘                                         │
└──────────────────────────────────────────────────────────────────────────┘
                  ▲                                    │
                  │ REST                               │ TCP/UDP
                  │                                    ▼
┌─────────────────┴───────────────┐      ┌──────────────────────────────┐
│ MaskEditorApp (Python/WebView)  │      │ 下游：使用 EdgeLinkSdk 的     │
│  · 離線設計 Mask 定義 JSON       │      │ Unity / .NET / 桌面 App       │
└─────────────────────────────────┘      └──────────────────────────────┘
```

---

## 4. 模組說明

### 4.1 NetworkServer（[Assets/Scripts/NetworkServer/](Assets/Scripts/NetworkServer/)）

底層通訊核心，採分層設計：

| 子目錄 | 職責 |
|---|---|
| [Main/](Assets/Scripts/NetworkServer/Main/) | [Main.cs](Assets/Scripts/NetworkServer/Main/Main.cs) — MonoBehaviour 啟動點、全域例外處理、HTTP 伺服器生命週期 |
| [Base/](Assets/Scripts/NetworkServer/Base/) | `NetworkConnectorBase`、`AsyncMessageQueue`、`MonitorCounter`、`SafeExecution`、`DisposableBase` 等基礎設施 |
| [Connector/](Assets/Scripts/NetworkServer/Connector/) | `NetworkConnectorCore` — 封裝 TCP/UDP 連線的建立、關閉、列舉 |
| [TCP/](Assets/Scripts/NetworkServer/TCP/) | `TCPServerConnector` / `TCPClientConnector`、`TcpClientRetryConfig`（指數退避重連） |
| [Udp/](Assets/Scripts/NetworkServer/Udp/) | `UdpConnector` / `UdpData` |
| [Router/](Assets/Scripts/NetworkServer/Router/) | [NetworkMessageRouter.cs](Assets/Scripts/NetworkServer/Router/NetworkMessageRouter.cs) — 將 TCP Server 收到的訊息依 ProtocolName 路由到對應 TCPClient，並套用各 Client 自有的 Mask |
| [Services/](Assets/Scripts/NetworkServer/Services/) | `PortDataStorageService`、`MaskDefinitionStorageService` — 設定檔持久化 |
| [Logging/](Assets/Scripts/NetworkServer/Logging/) | `LogHelper`、`MonitorManager`、`RouterLogHelper` |

關鍵流程（資料路徑）：

```
TCPServer 收到封包
  → NetworkMessageRouter.RouteMessageAsync
  → 依 ProtocolName 找出所有目標 TCPClient
  → 對每個 Client 取得其 MaskType → MaskDefinitionManager
  → MaskProcessor.Process(def, rawBytes, parsedMessage)
  → 透過 TCPClient 轉送到遠端
```

### 4.2 Mask 系統

- **MaskDefinition** — 描述訊息格式：`inputEncoding` (`text` / `binary`)、`fieldDelimiter`、`kvSeparator`、`binaryFields[]`、`outputTemplate` (如 `"ID={ID};TEMP={TEMP}"`)、`sampleData`
- **MaskProcessor** ([Assets/Scripts/Mask/MaskProcessor.cs](Assets/Scripts/Mask/MaskProcessor.cs)) — Unity 端套用遮罩：解析欄位 → 填入模板
- **MaskDefinitionManager** ([Assets/Scripts/MaskDefinitionManager.cs](Assets/Scripts/MaskDefinitionManager.cs)) — 單例，以 `Setting/MaskDefinitions.setting` 為後援儲存，預設保留 `OriginalData` (直接轉發 `{raw}`)

支援資料型別：`uint8`、`uint16_le`、`uint16_be`、`int32_le`、`float_le`、`hex`。

### 4.3 Web API（[Assets/Scripts/WebApi/](Assets/Scripts/WebApi/)）

內建 `HttpListener`，預設 `http://localhost:8181/`，由 `ApiRouter` 派發：

| Method | Endpoint | 功能 |
|---|---|---|
| GET | `/` | 回傳 [IOT-Server/WebUI/index.html](IOT-Server/WebUI/index.html) |
| GET / POST / DELETE | `/api/ports` | 連線埠的讀取、新增、刪除 |
| POST | `/api/ports/{name}/mask` | 變更指定連線埠的遮罩 |
| GET / POST | `/api/masks` | 遮罩清單 / 新增 |
| GET / PUT / DELETE | `/api/masks/{id}` | 單一遮罩的讀/寫/刪除 |
| POST | `/api/masks/{id}/rename` | 遮罩更名 |
| GET | `/api/logs`、`/api/monitor-logs` | 日誌查詢 |
| GET | `/api/monitor-stream` | SSE，即時推送監控事件 |
| GET / POST / DELETE | `/api/monitor/port` | 切換監控目標 |
| GET / POST | `/api/language` | 多語系切換 |

所有路由皆支援 CORS (`Access-Control-Allow-Origin: *`)。

### 4.4 EdgeLinkSdk（[IOT-Server/EdgeLinkSdk/](IOT-Server/EdgeLinkSdk/)）

獨立 .NET 類別庫，供 **下游** 應用 (Unity / WPF / Console) 直接消費 IoT Server 處理完的訊息。

核心 API：

| 類別 | 說明 |
|---|---|
| `EdgeLinkReceiver(Protocol, MaskDefinition?)` | 統一接收器；Protocol 選 `TCP` 或 `UDP` |
| `.Start(int port)` / `.Stop()` | 啟動 / 停止監聽 |
| `.Flush()` | 在 `Update()` 呼叫，讓事件切回 Unity 主執行緒 |
| 事件 `OnMessage` / `OnConnectionChanged` / `OnError` | 訊息、連線狀態、錯誤回呼 |
| `MaskDefinition.FromJsonFile(path)` | 從 MaskEditor 匯出的 JSON 反序列化 |
| `MaskParser.Parse` / `.ParseBinary` / `.ParseOutput` | 正向解析、二進位解析、反向欄位擷取 |

範例位於 [IOT-Server/EdgeLinkSdk/EdgeLinkExample.cs](IOT-Server/EdgeLinkSdk/EdgeLinkExample.cs)，測試於 [IOT-Server/EdgeLinkSdk.Tests/](IOT-Server/EdgeLinkSdk.Tests/)。

### 4.5 MaskEditorApp（[MaskEditorApp/](MaskEditorApp/)）

獨立 Python 桌面應用（`pywebview` + EdgeChromium），載入 [MaskEditor.html](IOT-Server/WebUI/MaskEditor.html)，提供 **離線** 遮罩設計、匯出為 JSON；靠 tkinter 呼叫系統存檔對話框。打包由 `build.bat` + PyInstaller 產出單一 EXE。

相依：`pywebview>=4.4`、`pyinstaller>=6.0`、`pillow>=10.0`。

### 4.6 UI（Unity 內）

[Assets/Scripts/UI/](Assets/Scripts/UI/) 採 MV-builder 分層：

- `Core/NetworkPortManager` — 管理所有連線的資料與事件
- `Builder/` — `PortTableFactory` / `PortTableBinder` / `PortTableSpawner`（表格產生）
- `View/NetworkPortTableUIManager`、`NetworkSettingsUI`
- `Controller/PortTableController`
- `Monitor/` — `Monitor`、`MonitorConsole`、`MonitorLog`
- `Widgets/Table`

---

## 5. 設定檔

| 檔案 | 用途 |
|---|---|
| [Setting/PortDatas.setting](Setting/PortDatas.setting) | 連線埠清單（執行期會更新） |
| [Setting/MaskDefinitions.setting](Setting/MaskDefinitions.setting) | 所有遮罩定義；首次執行會插入預設 `OriginalData` |
| [Setting/TcpClientRetryConfig.setting](Setting/TcpClientRetryConfig.setting) | TCP Client 重試策略：`InitialDelayMs=1000`、`MaxDelayMs=10000`、`HeartbeatIntervalMs=1000`；`-1` 代表無限重試 |
| [Assets/portData.json](Assets/portData.json) | 範例 Port 配置（開發用） |
| [BuildSetting.json](BuildSetting.json) | 自訂建置腳本設定：輸出到 `C:/Projects/Builds`，並複製 `GameData/ · Setting/ · IOT-Server/` 三個資料夾 |

---

## 6. 建置與執行

### 6.1 Unity 應用 (EdgeLink Server 本體)

1. 使用 Unity（版本對應 `Packages/manifest.json` 與 `ProjectSettings/ProjectVersion.txt`）開啟專案根目錄
2. 開啟場景 `Assets/Scenes/Main.unity`
3. 直接 Play，或透過專案內的 Build Setting (`EdgeLink Server` 型別) 打包
4. 預設 Web UI：<http://localhost:8181/>

### 6.2 EdgeLinkSdk（供下游專案使用）

```bash
dotnet build IOT-Server/EdgeLinkSdk/EdgeLinkSdk.csproj -c Release
dotnet test  IOT-Server/EdgeLinkSdk.Tests/EdgeLinkSdk.Tests.csproj
```

安裝到下游 Unity 專案：

1. 將 `EdgeLinkSdk.dll` 與相依 DLL 放入 `Assets/Plugins/`
2. 把 MaskEditor 匯出的 `sensor.json` 放入 `Assets/StreamingAssets/`
3. 將 [EdgeLinkExample.cs](IOT-Server/EdgeLinkSdk/EdgeLinkExample.cs) 掛到任一 GameObject
4. 在 IoT Server Web UI 新增 TCP Client，Target IP = 使用者機器、Port = 例中 `listenPort`

### 6.3 MaskEditorApp（桌面版遮罩編輯器）

```bash
cd MaskEditorApp
pip install -r requirements.txt
python main.py
# 打包：
./build.bat
```

---

## 7. 開發日誌重點（近期 commits）

- `9530727` — EdgeLinkSdk 建置產物與路徑設定更新
- `514bbab` — 專案設定：品牌名稱、App Icon、字型、遮罩清理
- `d53e336` — Unity 相容修正、WebUI Port 表單多語系、使用說明書 PDF
- `95e1fab` — EdgeLinkExample 改用 `FromJsonFile` API
- `2becc7c` — EdgeLinkSdk 精簡：移除舊 Receiver、加入 `FromJson`

## 8. 文件與資源

- 使用說明書：[docs/EdgeLinkServer_Manual_zh-TW.pdf](docs/EdgeLinkServer_Manual_zh-TW.pdf)、[docs/EdgeLinkServer_Manual_en-US.pdf](docs/EdgeLinkServer_Manual_en-US.pdf)
- 手冊產生器：[gen_manual.py](gen_manual.py)
- 圖示產生器：[gen_app_icon.py](gen_app_icon.py)、[MaskEditorApp/gen_icon.py](MaskEditorApp/gen_icon.py)

## 9. 潛在改進建議

1. **測試覆蓋** — 目前測試集中在 SDK 側 (`EdgeLinkSdk.Tests`)；Unity 端的 Router / MaskProcessor / PortManager 缺 EditMode/PlayMode 測試。
2. **API 認證** — `HttpApiServer` 只綁 `localhost` 且無 token/auth；若未來要開放遠端 Web UI，至少需 Bearer Token + HTTPS 前置代理。
3. **設定檔校驗** — `.setting` 為裸 JSON，載入時缺 schema/版本號，升級時容易踩雷，建議加 `version` 欄與 migration 流程。
4. **Mask 模板語法** — 目前 `{field}` 佔位符用 `StringBuilder.Replace` 實作，若欄位值本身含 `{}` 會有邊界問題；可考慮正則或 tokenizer。
5. **日誌與監控** — `MonitorCounter` / SSE 已具備雛形，可延伸匯出 Prometheus 指標或 CSV 供離線分析。

---

*本文件由自動分析產出，如專案結構異動請同步更新。*
