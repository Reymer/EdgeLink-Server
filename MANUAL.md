# EdgeLink Server 使用說明書

**版本：1.0 · 平台：Windows 10/11 x64 · 語言：.NET 8**

---

## 目錄

1. [產品概述](#1-產品概述)
2. [安裝與啟動](#2-安裝與啟動)
3. [登入與安全](#3-登入與安全)
4. [Web 管理介面](#4-web-管理介面)
5. [Port 管理](#5-port-管理)
6. [Mask 系統](#6-mask-系統)
7. [訊息路由](#7-訊息路由)
8. [請求模式](#8-請求模式)
9. [即時監控](#9-即時監控)
10. [設定匯出 / 匯入](#10-設定匯出--匯入)
11. [CLI 選項](#11-cli-選項)
12. [API 參考](#12-api-參考)
13. [常見情境範例](#13-常見情境範例)
14. [疑難排解](#14-疑難排解)

---

## 1. 產品概述

EdgeLink Server 是一個輕量化的 IoT 協定橋接器，執行於 Windows 上。  
它的核心功能是：

- 讓 **TCP Server**（設備端）收到的訊息，透過 **Mask**（資料轉換規則）處理後，自動路由到 **TCP Client**（系統端）
- 回傳方向亦同：系統端的回應可以轉換後送回給設備
- 所有 Port 和 Mask 都可在瀏覽器介面即時設定，無需重啟服務

```
設備 (IoT Device)
    │  TCP 連線
    ▼
┌─────────────────────────────────┐
│  EdgeLink TCP Server Port       │
│  (監聽指定 port，接受設備連線)    │
│           │ SourceProtocolId 路由│
│  EdgeLink TCP Client Port       │
│  (主動連出，轉發給系統端)         │
└─────────────────────────────────┘
    │  TCP 連線
    ▼
系統端 / 後端服務
```

---

## 2. 安裝與啟動

### 2.1 直接執行

從 Releases 下載 `EdgeLink-Server-win-x64.zip`，解壓縮後執行：

```
EdgeLinkServer.exe
```

預設以 **HTTPS** 啟動（自動產生自簽憑證），瀏覽器開啟：

```
https://localhost:8443
```

> 瀏覽器第一次會顯示「安全性警告」，因為使用自簽憑證。點「進階 → 繼續前往」即可。

### 2.2 僅使用 HTTP

```
EdgeLinkServer.exe --no-https
```

開啟：`http://localhost:8080`

### 2.3 自訂 Port

```
EdgeLinkServer.exe --port 9090 --https-port 9443
```

### 2.4 首次執行說明

- 首次執行時自動建立 `Data/` 和 `Setting/` 目錄
- 憑證儲存於 `Data/server.pfx`，有效期 10 年
- 預設帳號密碼：`admin`（首次登入後請立即修改）

---

## 3. 登入與安全

### 3.1 登入

開啟瀏覽器前往管理介面，輸入密碼後登入。

- Session 有效期：**8 小時**（每次操作自動延長）
- Cookie 設定：`HttpOnly`、`SameSite=Strict`（HTTPS 時加 `Secure`）

### 3.2 修改密碼

登入後，點右上角選單 → **Change Password**。

- 需輸入目前密碼才能修改
- 修改後**所有已登入的 session 立即失效**，需重新登入
- 密碼以 PBKDF2-SHA256（100,000 次迭代）儲存，無明文

### 3.3 HTTPS 與憑證

- 憑證自動包含所有本機 IP（SAN），可在同網段的電腦直接訪問
- 憑證資訊儲存於 `Data/server.pfx`（不含於版控）
- 若憑證損毀，刪除 `Data/server.pfx` 後重啟即自動重建

### 3.4 CORS

若前端頁面與伺服器不同源，需設定允許的來源：

```
EdgeLinkServer.exe --cors https://myapp.example.com,http://localhost:3000
```

---

## 4. Web 管理介面

管理介面共三個主要頁面：

| 頁籤 | 功能 |
|------|------|
| **Ports** | 管理所有 Port（新增、刪除、啟用、即時監控連線狀態與流量） |
| **Mask** | 管理資料轉換規則（建立、編輯、刪除 Mask 定義） |
| **System Log** | 查看伺服器事件記錄，支援關鍵字搜尋 |

---

## 5. Port 管理

### 5.1 Port 類型

| 類型 | 說明 |
|------|------|
| **TCP SERVER** | 開啟本機 TCP 監聽，等待設備連入；支援多個設備同時連線 |
| **TCP CLIENT** | 主動連出至外部系統；設備訊息透過此 Port 路由轉發 |
| **UDP** | UDP 接收端，無連線狀態；僅接收，不主動回應 |

### 5.2 新增 Port

點 **Ports → Add**，填入以下欄位：

| 欄位 | 說明 | 必填 |
|------|------|------|
| Protocol Name | 此 Port 的識別名稱（全系統唯一） | ✓ |
| Net Protocol | TCP SERVER / TCP CLIENT / UDP | ✓ |
| Local Port | 本機監聽 port（TCP SERVER / UDP 需填；TCP CLIENT 填 `--`） | 視類型 |
| Target IP | 遠端 IP（TCP CLIENT 需填） | TCP CLIENT |
| Remote Port | 遠端 port（TCP CLIENT 需填） | TCP CLIENT |
| Mask Type | 傳出訊息套用的遮罩規則（預設 OriginalData） | |
| Response Mask Type | 回應訊息套用的遮罩規則 | |
| Request Mode | serial / polling / concurrent（詳見第 8 節） | |
| Source Protocol | 路由來源 Port（TCP CLIENT 才有意義，詳見第 7 節） | |

### 5.3 啟用 / 停用

點 Port 列表中的開關即可即時啟用或停用，無需刪除。  
停用後設備連線將被拒絕，設定保留。

### 5.4 刪除 Port

停用中或啟用中的 Port 均可刪除，刪除後設定永久移除。

### 5.5 查看連線 Client（TCP SERVER）

TCP SERVER Port 展開後可看到目前所有連線設備的詳細資訊：

| 欄位 | 說明 |
|------|------|
| Endpoint | 設備的 IP:Port |
| Connected | 連線持續時間（秒） |
| Last Activity | 最後收到資料的時間 |
| Messages | 收到的訊息筆數 |
| Total Bytes | 收到的總位元組數 |
| Rate | 目前傳輸速率（bytes/sec） |
| RTT | PING/PONG 往返延遲（ms） |

### 5.6 TCP Keep-Alive（PING/PONG）

EdgeLink 會自動對每個連入的設備發送心跳：

- 首次心跳：連線後 **3 秒**
- 後續間隔：每 **5 秒**
- 格式：`EDGELINK_PING:<16位元16進位亂數>\n`
- 設備必須回覆：`EDGELINK_PONG:<相同16進位>\n`
- 連續 **3 次**未回應 → 斷線

> **開發設備端程式時必須實作 PING/PONG，否則約 15～20 秒後會被斷線。**

```python
# Python 範例：PING/PONG 處理
import socket, threading

def receive_loop(sock):
    buf = b""
    while True:
        data = sock.recv(1024)
        if not data:
            break
        buf += data
        while b"\n" in buf:
            line, buf = buf.split(b"\n", 1)
            msg = line.decode().strip()
            if msg.startswith("EDGELINK_PING:"):
                hex_val = msg.split(":")[1]
                sock.sendall(f"EDGELINK_PONG:{hex_val}\n".encode())
            else:
                print("Received:", msg)

sock = socket.socket()
sock.connect(("127.0.0.1", 9001))
threading.Thread(target=receive_loop, args=(sock,), daemon=True).start()
sock.sendall(b"id:DEV01;value:36.5\n")
```

### 5.7 TCP Client 重連策略

TCP CLIENT 斷線後自動重連，策略如下：

| 參數 | 預設值 | 說明 |
|------|--------|------|
| 初始重試次數 | 無限 | 首次連線失敗時的最大重試次數（-1 = 無限） |
| 後續重試次數 | 無限 | 斷線後重連的最大次數（-1 = 無限） |
| 初始等待 | 1,000 ms | 第一次重試前的等待時間 |
| 最大等待 | 30,000 ms | 指數退避上限 |
| 心跳間隔 | 5,000 ms | PING 間隔 |

---

## 6. Mask 系統

Mask（遮罩）定義了如何將原始訊息解析並轉換成新格式後再轉發。

### 6.1 Mask 的作用位置

```
設備送出: "id:DEV01;temp:36.5"
                │
         ┌──────▼──────┐
         │ Request Mask │  ← TCP Client 的 Mask Type
         └──────┬──────┘
                │ 轉換後
                ▼
     系統端收到: {"deviceId":"DEV01","temperature":36.5}
```

回應方向：

```
系統端回覆: "status:ok;code:200"
                │
         ┌──────▼───────┐
         │ Response Mask │  ← TCP Client 的 Response Mask Type
         └──────┬───────┘
                │ 轉換後
                ▼
     設備收到: "OK:ok"
```

> **Request Mask** 和 **Response Mask** 都設定在 **TCP Client Port** 上，不是 TCP Server。

### 6.2 Mask 定義欄位

| 欄位 | 說明 | 範例 |
|------|------|------|
| Mask ID | 唯一識別名稱 | `SensorData` |
| Field Delimiter | 欄位分隔符（預設 `;`） | `;` |
| KV Separator | 鍵值分隔符（預設 `:`） | `:` |
| Output Template | 輸出模板，以 `{欄位名}` 作為佔位符 | `{"id":"{id}","val":{val}}` |
| Sample Data | 範例輸入（供測試用，不影響運作） | `id:DEV01;val:36.5` |
| Route Mode | 回應路由模式（broadcast / response）| `broadcast` |
| Correlation ID Field | concurrent 模式使用的 ID 欄位名 | `_corrId` |

### 6.3 OriginalData（內建 Mask）

所有系統內建的特殊 Mask，**不可刪除**。

- Output Template 為 `{raw}`
- 訊息不做任何轉換，原樣轉發

### 6.4 欄位解析規則

以輸入 `id:DEV01;temp:36.5;unit:C` 為例，預設分隔符（`;` / `:`）：

```
解析結果：
  id   → "DEV01"
  temp → "36.5"
  unit → "C"
```

模板 `{"id":"{id}","temp":{temp},"unit":"{unit}"}` 輸出：

```json
{"id":"DEV01","temp":36.5,"unit":"C"}
```

### 6.5 模板規則

- 佔位符格式：`{欄位名}`
- **任何一個**佔位符對應的欄位不存在於訊息中 → 整筆訊息**丟棄**（不轉發）
- 若 Output Template 為空或 `{raw}` → 原樣轉發

### 6.6 Route Mode（回應路由模式）

此設定在 **Response Mask** 的定義中設定，控制回應如何發送回設備：

| 模式 | 說明 |
|------|------|
| `broadcast`（預設）| 所有已連線的設備都收到回應 |
| `response` | 只有發出此次請求的設備收到回應 |

### 6.7 新增與編輯 Mask

1. 點 **Mask → Add** 輸入 Mask ID
2. 點 Mask 名稱進入編輯畫面
3. 填入欄位分隔符、KV 分隔符、輸出模板
4. 輸入範例資料後點 **Preview** 可即時預覽轉換結果
5. 點 **Save** 儲存

---

## 7. 訊息路由

### 7.1 基本路由設定

路由的關鍵是在 **TCP Client Port** 上設定 `Source Protocol`，指向對應的 **TCP Server Port**。

```
TCP Server Port（設備連入）
    │  Protocol Name: "DeviceServer"
    │  ID: "a1b2c3d4"
    │
TCP Client Port（路由目標）
    │  Source Protocol: "DeviceServer"（選擇上面的 Server）
    │  Target IP: 192.168.1.100
    │  Remote Port: 9001
    ▼
遠端系統
```

### 7.2 一對多路由（廣播）

同一個 TCP Server 可以路由到**多個** TCP Client，訊息會同時廣播到所有目標：

```
TCP Server "DeviceServer"
    ├──▶ TCP Client A → 系統A
    ├──▶ TCP Client B → 系統B
    └──▶ TCP Client C → 系統C
```

設定方式：建立多個 TCP Client Port，每個都將 Source Protocol 指向同一個 TCP Server。

### 7.3 路由流程

```
設備送訊息
    ↓
TCP Server 接收
    ↓
查找所有 SourceProtocolId = 此 Server 的 TCP Client
    ↓
對每個 TCP Client：
  1. 取得該 Client 的 Mask Type
  2. 套用 Mask 轉換訊息
  3. 若轉換結果為空 → 丟棄（不轉發）
  4. 依 Request Mode 決定傳送策略
    ↓
送出至遠端系統
    ↓
遠端系統回應
    ↓
套用 Response Mask
    ↓
依 Route Mode 決定回送對象（broadcast / response）
    ↓
送回設備
```

---

## 8. 請求模式

Request Mode 控制 TCP Client 如何處理同時進來的多個請求，設定在 **TCP Client Port** 上。

### 8.1 Serial（序列，預設）

```
設備A → 請求1 ─┐
設備B → 請求2 ─┤→ [FIFO 佇列] → 一次送出一個 → 等回應 → 下一個
設備C → 請求3 ─┘
```

- 嚴格按照到達順序處理
- 每個請求等待回應（最多 5 秒 timeout）後才處理下一個
- 不會遺漏任何訊息
- **適用：設備需要確認回應、不容許訊息遺漏的場景**

### 8.2 Polling（輪詢）

```
設備 → 請求1
設備 → 請求2  ← 覆蓋請求1
設備 → 請求3  ← 覆蓋請求2
        只保留最新的請求3，送出
```

- 只保留最新一筆資料
- 中間的請求會被丟棄
- 適合感測器持續傳送資料、只需要最新值的場景
- **適用：溫度、位置等持續更新、歷史資料不重要的場景**

### 8.3 Concurrent（並發）

```
設備A → 請求1 → 注入 correlationId=AAAA → 送出
設備B → 請求2 → 注入 correlationId=BBBB → 送出
設備C → 請求3 → 注入 correlationId=CCCC → 送出
(同時)
遠端回應 correlationId=BBBB → 路由回設備B
遠端回應 correlationId=AAAA → 路由回設備A
```

- 多個請求同時進行，不排隊
- EdgeLink 自動在訊息中注入 8 字元的 `correlationId`（欄位名由 Mask 定義中的 **Correlation ID Field** 設定）
- 遠端系統回應時必須帶回相同的 correlationId
- EdgeLink 根據 correlationId 將回應路由到正確的設備

**Concurrent 模式設定步驟：**

1. 建立 Request Mask，模板中加入 `{_corrId}`：
   ```
   {id}:{val}:{_corrId}
   ```
2. 建立 Response Mask，設定：
   - Route Mode: `response`
   - Correlation ID Field: `_corrId`（遠端回應中帶有 correlationId 的欄位名）
3. TCP Client Port 設定：
   - Request Mode: `concurrent`
   - Mask Type: 上面的 Request Mask
   - Response Mask Type: 上面的 Response Mask

**適用：多設備同時發送請求、各自需要收到對應回應的場景（如多把感測槍同時掃描）**

---

## 9. 即時監控

### 9.1 Monitor（Port 監控）

在 Port 列表中點選任一 Port 的「Monitor」按鈕，即可開啟該 Port 的即時訊息串流：

- 顯示所有進出訊息（含原始格式）
- 支援關鍵字搜尋
- 可下載目前日誌

監控的訊息格式：
```
[#001] [Router] [TCP Server | DeviceServer] [from 192.168.1.50:12345] Received: id:DEV01;temp:36.5
[#002] [Router] [TCP Client | BackendClient] Sent: {"id":"DEV01","temp":36.5}
```

### 9.2 System Log

**System Log** 頁面顯示伺服器自身的事件記錄：

- Port 啟動 / 停止
- 設備連線 / 斷線
- 錯誤訊息
- 支援關鍵字篩選
- 記錄保留 7 天，每日一個檔案（`Data/Logs/`）

---

## 10. 設定匯出 / 匯入

### 10.1 匯出

點 **System → Export Settings**，下載目前所有 Port 和 Mask 定義為 JSON 檔案。

> OriginalData（內建 Mask）不會出現在匯出檔案中。

### 10.2 匯入

點 **System → Import Settings**，選擇先前匯出的 JSON 檔案。

匯入行為：
- **Mask**：若 ID 不存在則新增；若已存在則**跳過**
- **Port**：若 Protocol Name 不重複且 Port 號不衝突則新增；否則**跳過**
- 匯入不會刪除現有設定

---

## 11. CLI 選項

```
EdgeLinkServer.exe [選項]

選項：
  --port <n>          HTTP 監聽 port（預設：8080）
  --no-https          停用 HTTPS（預設啟用）
  --https-port <n>    HTTPS 監聽 port（預設：8443）
  --cors <origins>    允許的 CORS 來源，逗號分隔

環境變數（優先順序低於 CLI 參數）：
  EDGELINK_PORT           HTTP port
  EDGELINK_HTTPS          設為 "0" 可停用 HTTPS
  EDGELINK_HTTPS_PORT     HTTPS port
  EDGELINK_CORS           CORS 來源

優先順序：CLI 參數 > 環境變數 > 預設值
```

---

## 12. API 參考

Base URL：`http(s)://<host>:<port>`

所有 `/api/*` 端點（除 `/api/auth/login`、`/api/auth/logout`、`/api/auth/status`）都需要登入後才能呼叫。

### 認證

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/auth/login` | 登入，Body: `{"password":"..."}` |
| POST | `/api/auth/logout` | 登出 |
| GET | `/api/auth/status` | 檢查登入狀態 |
| POST | `/api/auth/change-password` | 修改密碼，Body: `{"currentPassword":"...","newPassword":"..."}` |

### Port 管理

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/ports` | 取得所有 Port 列表 |
| POST | `/api/ports` | 新增 Port |
| PUT | `/api/ports/{id}` | 更新 Port |
| DELETE | `/api/ports` | 刪除 Port，Body: `{"id":"..."}` |
| POST | `/api/ports/{id}/enabled` | 啟用/停用，Body: `{"enabled":true}` |
| POST | `/api/ports/{id}/mask` | 更換 Mask，Body: `{"maskType":"..."}` |
| GET | `/api/ports/{id}/clients` | 取得 TCP SERVER 的連線 Client 列表 |

**新增 Port 請求體：**

```json
{
  "protocolName": "DeviceServer",
  "netProtocol": "TCP SERVER",
  "localPort": "9001",
  "targetIp": "",
  "remotePort": "--",
  "maskType": "OriginalData",
  "responseMaskType": "OriginalData",
  "requestMode": "serial",
  "sourceProtocolId": "",
  "sourceProtocolName": ""
}
```

**新增成功回應（201）：**

```json
{ "success": true, "id": "a1b2c3d4" }
```

### Mask 管理

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/masks` | 取得所有 Mask ID 列表 |
| POST | `/api/masks` | 新增 Mask，Body: `{"maskId":"..."}` |
| GET | `/api/masks/{maskId}` | 取得 Mask 定義 |
| PUT | `/api/masks/{maskId}` | 儲存 Mask 定義 |
| DELETE | `/api/masks/{maskId}` | 刪除 Mask |
| POST | `/api/masks/{maskId}/rename` | 重新命名，Body: `{"newId":"..."}` |

**Mask 定義格式：**

```json
{
  "maskId": "SensorData",
  "fieldDelimiter": ";",
  "kvSeparator": ":",
  "outputTemplate": "{\"id\":\"{id}\",\"temp\":{temp}}",
  "sampleData": "id:DEV01;temp:36.5",
  "routeMode": "broadcast",
  "correlationIdField": "",
  "localizationKey": "",
  "description": ""
}
```

### 監控

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/monitor-stream` | SSE 即時串流（text/event-stream） |
| POST | `/api/monitor/port` | 設定監控 Port，Body: `{"id":"..."}` |
| DELETE | `/api/monitor/port` | 清除監控 Port |
| GET | `/api/monitor/port` | 取得目前監控的 Port |
| GET | `/api/logs` | 系統日誌（支援 `?cursor=<n>` 分頁） |
| GET | `/api/monitor-logs` | 監控日誌（支援 `?cursor=<n>` 分頁） |

### 設定

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/settings/export` | 匯出所有設定 JSON |
| POST | `/api/settings/import` | 匯入設定 JSON |

---

## 13. 常見情境範例

### 情境 A：IoT 感測器 → 後端伺服器（單向轉發）

**設備傳送格式：** `id:SENSOR01;temp:25.3;humidity:60`

**目標：** 後端收到 JSON `{"device":"SENSOR01","temp":25.3,"humidity":60}`

**設定步驟：**

1. **建立 Mask** `SensorJSON`：
   - Field Delimiter: `;`
   - KV Separator: `:`
   - Output Template: `{"device":"{id}","temp":{temp},"humidity":{humidity}}`

2. **新增 TCP Server Port**：
   - Protocol Name: `SensorServer`
   - Local Port: `9001`

3. **新增 TCP Client Port**：
   - Protocol Name: `BackendClient`
   - Target IP: `192.168.1.100`
   - Remote Port: `8080`
   - Mask Type: `SensorJSON`
   - Source Protocol: `SensorServer`

---

### 情境 B：多台設備 → 多個前端（並發廣播）

**場景：** 多把感應槍同時掃描，結果分別推送到各自的前端顯示器

**設定：**

1. 建立一個 TCP Server（`GunServer`, port 9001）
2. 為每個前端建立一個 TCP Client，Source Protocol 都指向 `GunServer`
3. Request Mode: `serial`（每把槍輪流等回應）
4. Response Mask Route Mode: `response`（回應只送回對應的槍端）

或使用 **concurrent** 模式讓多把槍同時發送請求，各自收到回應。

---

### 情境 C：設備查詢，需要回應（Request-Response）

**流程：** 設備送查詢 → 後端處理 → 回應送回原設備

**設定：**

1. TCP Client Port：
   - Request Mode: `serial`
   - Response Mask Route Mode: `response`

2. 後端收到訊息後直接在同一個 TCP 連線上回覆即可

---

### 情境 D：純資料收集（設備不需要回應）

**設定：**

- TCP Client Port：
  - Request Mode: `serial`
  - Response Mask: `OriginalData`（若後端不回覆，逾時後自動繼續）

或後端完全不回覆訊息。

---

## 14. 疑難排解

### 設備連線後約 15 秒斷線

**原因：** 未實作 PING/PONG 回應。  
**解法：** 在設備程式中加入接收 `EDGELINK_PING:` 並回覆 `EDGELINK_PONG:` 的邏輯（詳見第 5.6 節）。

---

### 瀏覽器顯示「無法連線」

**可能原因與解法：**

| 情況 | 解法 |
|------|------|
| 剛啟動，程式還在初始化 | 等待 3～5 秒後重新整理 |
| Port 已被其他程式佔用 | 用 `--port` 換一個 port |
| 防火牆阻擋 | 允許 EdgeLinkServer.exe 或指定 port |
| 無系統管理員權限（LAN 存取） | 以管理員身分執行，或設定 URL ACL |

---

### 訊息沒有被路由到後端

**檢查清單：**

1. TCP Client Port 的 **Source Protocol** 有沒有指向正確的 TCP Server
2. TCP Client Port 是否為**啟用**狀態
3. TCP Client Port 是否顯示**已連線**（IsConnected = true）
4. **Mask 設定**：若模板中有佔位符，檢查訊息是否包含所有欄位

---

### 訊息有到後端但格式不對

**檢查：** 在 **Monitor** 頁面即時查看訊息的原始格式和轉換後格式，確認 Mask 設定是否正確。

---

### 後端回應沒有送回設備

**檢查清單：**

1. Response Mask 的 **Route Mode**：
   - `broadcast`：所有設備都收 → 設備有沒有收到？
   - `response`：只有發送請求的設備收 → Request Mode 是否為 `serial` 或 `concurrent`？
2. Concurrent 模式：後端回應是否有帶回 correlationId 欄位？
3. Serial 模式：5 秒內沒有收到回應會逾時，下一筆請求才會送出

---

### 設定存到哪裡？

```
EdgeLinkServer.exe 所在目錄
├── Setting/
│   ├── PortDatas.setting       ← Port 設定
│   └── MaskDefinitions.setting ← Mask 設定
└── Data/
    ├── auth.json               ← 密碼 hash
    ├── sessions.json           ← 登入 session
    ├── server.pfx              ← HTTPS 憑證
    ├── cert.pass               ← 憑證密碼
    └── Logs/                   ← 每日日誌檔
        └── 2024-01-15.log
```

> `Data/server.pfx` 和 `Data/cert.pass` 不應進入版本控制（已在 `.gitignore`）。

---

*EdgeLink Server · GPL-3.0 · Extrakyo*
