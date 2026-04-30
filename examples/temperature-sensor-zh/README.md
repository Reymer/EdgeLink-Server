# 範例：溫度感測器

示範完整的端對端資料流：

```
device.py  →  EdgeLink :9001  →  [SensorMask]  →  EdgeLink :9002  →  receiver.py
```

模擬的 IoT 感測器傳送原始資料 `TEMP:24.5;HUM:62.3` 給 EdgeLink。
EdgeLink 透過 **SensorMask** 解析欄位，將格式化後的結果轉發給接收端。

---

## 需求

- Python 3.8 以上
- EdgeLink Server 已啟動（預設 `localhost:8080`）

---

## 快速開始

### 步驟一 — 設定 EdgeLink

執行設定腳本，自動建立所需的 Mask 與 Port：

```bash
python setup.py
```

若 EdgeLink 不在本機，可指定參數：

```bash
python setup.py --host 192.168.0.10 --port 8080 --password 你的密碼
```

此腳本會建立：
- **SensorMask** — 解析 `TEMP`/`HUM` 欄位，格式化輸出
- **感測器輸入** — TCP Server，埠號 `9001`（設備連入）
- **感測器輸出** — TCP Client，埠號 `9002`（轉發至接收端）

### 步驟二 — 啟動接收端

開啟終端機 1：

```bash
python receiver.py
```

### 步驟三 — 啟動感測器模擬器

開啟終端機 2：

```bash
python device.py
```

---

## 預期輸出

**device.py**
```
溫度感測器 — 連線至 127.0.0.1:9001...
✓ 已連線至 EdgeLink 127.0.0.1:9001
  每 1.0 秒傳送一次資料（Ctrl+C 停止）

  → 已送出：TEMP:24.5;HUM:62.3
  → 已送出：TEMP:25.1;HUM:60.8
  → 已送出：TEMP:23.9;HUM:63.1
```

**receiver.py**
```
資料接收端 — 監聽 127.0.0.1:9002
  等待 EdgeLink 連線...（Ctrl+C 停止）

✓ EdgeLink 已連線（來自 127.0.0.1:xxxxx）

  [14:32:01] 溫度：24.5°C｜濕度：62.3%
  [14:32:02] 溫度：25.1°C｜濕度：60.8%
  [14:32:03] 溫度：23.9°C｜濕度：63.1%
```

---

## 運作原理

### SensorMask 定義

| 欄位 | 設定值 |
|------|--------|
| 欄位分隔符 | `;` |
| KV 分隔符 | `:` |
| 輸出範本 | `溫度：{TEMP}°C｜濕度：{HUM}%` |
| 路由模式 | `broadcast` |

EdgeLink 將收到的訊息以 `;` 分割，再以 `:` 拆出鍵值對（`TEMP`、`HUM`），
最後套用輸出範本產生格式化結果。

### Port 設定

| 埠號 | 類型 | 方向 |
|------|------|------|
| 9001 | TCP Server | 設備 → EdgeLink |
| 9002 | TCP Client | EdgeLink → 接收端 |

兩個 Port 透過 `SourceProtocolId` 綁定，9001 收到的訊息
經 Mask 處理後自動轉發至 9002。

---

## 套用到真實設備

將 `device.py` 替換為你的實際設備：

1. 讓設備以 TCP 連線至 `<EdgeLink 位址>:9001`
2. 依照 `KEY:值;KEY:值` 格式傳送資料（或修改 Mask 配合你的格式）
3. 接收端連線至 `<EdgeLink 位址>:9002` 取得處理後的資料

若要修改資料格式，在 Web UI（`http://localhost:8080`）→ **Mask** 分頁 → **SensorMask** 編輯定義。
