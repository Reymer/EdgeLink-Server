<div align="center">

# EdgeLink Server

**IoT 訊息中繼與協定管理平台**

[![Unity](https://img.shields.io/badge/Unity-2022.3_LTS-black?logo=unity)](https://unity.com)
[![License](https://img.shields.io/badge/License-GPL_3.0-blue)](LICENSE)
[![OpenAPI](https://img.shields.io/badge/OpenAPI-3.0.3-6BA539?logo=openapiinitiative&logoColor=white)](http://localhost:8181/docs)
[![Version](https://img.shields.io/badge/Version-1.4.0-informational)]()

基於 Unity 執行，提供 TCP/UDP 多端口管理、遮罩協定定義、WebUI 操作介面與 SDK 整合。

</div>

---

## 功能特色

| 功能 | 說明 |
|------|------|
| **多協定支援** | TCP Server / TCP Client / UDP，每個端口獨立設定 |
| **遮罩系統** | 自訂欄位分隔符與 KV 分隔符，定義 IoT 韌體輸出格式並自動解析 |
| **訊息路由** | 廣播、單播、反向路由（回傳給來源設備） |
| **WebUI** | 瀏覽器操作介面，遮罩管理、Port 管理、系統日誌三分頁 |
| **EdgeLink SDK** | C# 接收端函式庫，可整合至 Unity 或任何 .NET 應用 |
| **Arduino Client** | Arduino Library，讓嵌入式設備直接串接 EdgeLink Server |
| **登入驗證** | WebUI 登入保護，Cookie Token-based 身份驗證 |
| **多語系** | 繁體中文 / English / 日本語 |

---

## 系統需求

| 項目 | 需求 |
|------|------|
| Unity | 2022.3 LTS 以上 |
| .NET | .NET 8（SDK 工具） |
| 瀏覽器 | Chrome / Edge（WebUI） |
| Arduino | Arduino IDE 1.8+ 或 2.x（EdgeLinkClient） |

---

## 快速開始

### 1. 啟動 Server

1. 用 Unity 開啟本專案
2. 執行 `Main` 場景
3. 瀏覽器開啟 `http://<本機IP>:8181` 進入 WebUI

> 從同網域其他裝置存取時，請用 `ipconfig` 查詢本機 IP，不要使用 `localhost`

### 2. 新增 Port

在 WebUI「Port 管理」分頁，點選「新增 Port」，填入：

- **協定名稱**：自訂識別名
- **類型**：TCP Server / TCP Client / UDP
- **連接埠**：監聽或連線的 Port 號
- **遮罩**：選擇對應的協定遮罩（選填）

### 3. 整合 EdgeLink SDK（C#）

```csharp
using EdgeLink;

var receiver = new EdgeLinkReceiver(Protocol.TCP);

receiver.OnMessage += msg =>
{
    Debug.Log(msg.Raw);
    // msg.Parsed.Fields["TEMP"] 等欄位（需搭配遮罩）
};

receiver.OnDeviceStatusChanged += (portName, endpoint, connected) =>
{
    Debug.Log($"{portName} {endpoint} {(connected ? "上線" : "離線")}");
};

receiver.Start(9090);

// 在 MonoBehaviour.Update() 呼叫：
receiver.Flush();
```

### 4. 整合 Arduino（C++）

```cpp
#include <EdgeLinkClient.h>

EdgeLinkClient client;

void setup() {
    client.begin("192.168.1.100", 5000);
}

void loop() {
    String data = "TEMP:" + String(readTemp()) + ";HUM:" + String(readHum());
    client.send(data);
    delay(1000);
}
```

> Arduino Library 位於 `tools/EdgeLinkClient/`，加入 Arduino IDE 後即可使用

---

## WebUI 分頁

| 分頁 | 功能 |
|------|------|
| 遮罩管理 | 新增 / 編輯 / 刪除遮罩，定義欄位解析規則與路由模式 |
| Port 管理 | 新增 / 刪除 / 批次刪除 Port，即時監控連線狀態與流量 |
| 系統日誌 | 查看伺服器運作日誌，支援關鍵字篩選 |

---

## API 文件

啟動 Server 後開啟互動式 API 文件：

```
http://localhost:8181/docs
```

原始規格檔（OpenAPI 3.0.3）：`http://localhost:8181/openapi.json`

<details>
<summary>端點一覽</summary>

Base URL：`http://<IP>:8181`

| 標籤 | 方法 | 路徑 | 說明 |
|------|------|------|------|
| Auth | `POST` | `/api/auth/login` | 登入 |
| Auth | `POST` | `/api/auth/logout` | 登出 |
| Auth | `GET` | `/api/auth/status` | 查詢登入狀態 |
| Auth | `POST` | `/api/auth/change-password` | 修改密碼 |
| Ports | `GET` | `/api/ports` | 取得所有 Port |
| Ports | `POST` | `/api/ports` | 新增 Port |
| Ports | `PUT` | `/api/ports/{id}` | 更新 Port |
| Ports | `DELETE` | `/api/ports` | 刪除 Port |
| Ports | `GET` | `/api/ports/{id}/clients` | 查詢 TCP 連線清單 |
| Masks | `GET` | `/api/masks` | 取得所有遮罩 |
| Masks | `POST` | `/api/masks` | 新增遮罩 |
| Masks | `PUT` | `/api/masks/{id}` | 更新遮罩定義 |
| Masks | `DELETE` | `/api/masks/{id}` | 刪除遮罩 |
| Monitor | `GET` | `/api/monitor-stream` | SSE 即時訊息串流 |
| Logs | `GET` | `/api/logs` | 系統日誌 |
| Settings | `GET` | `/api/settings/export` | 匯出設定 |
| Settings | `POST` | `/api/settings/import` | 匯入設定 |

</details>

---

## 專案結構

```
EdgeLink-Server/
├── Assets/
│   ├── Scripts/
│   │   ├── NetworkServer/       # TCP/UDP 核心、Router、Log
│   │   ├── Mask/                # 遮罩定義與解析
│   │   ├── WebApi/              # HTTP API Server（Auth、Port、Mask、SSE）
│   │   └── UI/                  # Unity UI 元件
│   └── GameData/                # 多語系語言鍵值
│
├── IOT-Server/
│   ├── WebUI/                   # 前端 HTML + OpenAPI 規格
│   ├── EdgeLinkSdk/             # C# 接收端 SDK
│   ├── ReceiverConsole/         # SDK 測試主控台（非 Unity 環境用）
│   └── DeviceSimulator/         # 模擬 IoT 設備發送資料
│
├── tools/
│   ├── EdgeLinkClient/          # Arduino Library
│   └── *.py                     # 整合測試 / 壓力測試腳本
│
└── Setting/                     # 執行期設定檔（Port、遮罩）
```

---

## 測試工具

```bash
pip install requests

python tools/run_all_tests.py          # 執行全部測試
python tools/stress_test.py            # 壓力測試
python tools/routing_integration_test.py  # 路由整合測試
```

---

## 版本紀錄

| 版本 | 內容 |
|------|------|
| v1.4.0 | OpenAPI 文件、Swagger UI、Auth 機制、設定匯入匯出 |
| v1.3.0 | 多語系、執行緒安全、WebUI 優化 |
| v1.2.0 | TCP UUID 追蹤、Log ID、遠端端口欄位 |
| v1.1.0 | 遮罩系統、KV 解析、即時預覽 |
| v1.0.0 | TCP/UDP Server/Client、基礎路由、WebUI |

---

<div align="center">

**Extrakyo**（昌霖）&nbsp;·&nbsp;GPL-3.0

</div>
