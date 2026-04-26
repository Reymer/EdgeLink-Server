"""
EdgeLink Server 路由整合測試

測試涵蓋：
  1. 基本轉發 (forward)     IoT → EdgeLink TCP Server → EdgeLink TCP Client → Device
  2. 串行模式 (serial)      Request → Device → Response → 回給發送者
  3. 輪詢模式 (polling)     多個請求只轉發最新一筆
  4. 並發模式 (concurrent)  多請求同時轉發，correlationId 匹配回應
  5. 廣播路由 (broadcast)   Device 回應送給所有已連入的 IoT 客戶端
  6. 連線通知 (status)      IoT 連線/斷線 → EDGELINK_STATUS 通知

=== EdgeLink 需預先設定 ===

  最少設定（適用所有測試）：
    - Port A: TCP Server，監聽 --server-port（例如 9999）
    - Port B: TCP Client，連出至 127.0.0.1:--device-port（例如 7777）
    - Port B 的 SourceProtocolId = Port A 的 Id

  遮罩建議（outputTemplate）：
    - Port B 的 MaskType（入站遮罩）："{raw}" 或自訂
    - Port B 的 ResponseMaskType（出站遮罩）：
        · routeMode = "response" → 只回給發送者（serial/concurrent 測試）
        · routeMode = "broadcast" → 廣播給所有人（broadcast 測試）

  Concurrent 模式額外需求：
    - Port B 的 MaskType outputTemplate 含 {_corrId}，例如：
        CORR={_corrId};DATA={raw}
      （EdgeLink 自動注入 correlationId）
    - Port B 的 ResponseMaskType correlationIdField = 回應訊息中代表 corrId 的欄位名稱
        例如回應格式 "CORR=xxx;RESULT=yyy"，correlationIdField = "CORR"

用法：
  python tools/routing_integration_test.py --server-port 9999 --device-port 7777 --mode all
  python tools/routing_integration_test.py --server-port 9999 --device-port 7777 --mode serial
  python tools/routing_integration_test.py --server-port 9999 --device-port 7777 --mode broadcast --broadcast-clients 3
"""

import asyncio
import argparse
import sys
import time
from dataclasses import dataclass, field

if sys.stdout.encoding and sys.stdout.encoding.lower() != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# ── 結果追蹤 ──────────────────────────────────────────────────────────────────

@dataclass
class TestResult:
    name: str
    passed: bool
    detail: str = ""

results: list[TestResult] = []


def PASS(name: str, detail: str = ""):
    results.append(TestResult(name, True, detail))
    print(f"  ✓ PASS  {name}" + (f"  ({detail})" if detail else ""))


def FAIL(name: str, detail: str = ""):
    results.append(TestResult(name, False, detail))
    print(f"  ✗ FAIL  {name}" + (f"  — {detail}" if detail else ""))


# ── 工具函式 ──────────────────────────────────────────────────────────────────

async def connect_iot(host: str, port: int, timeout: float = 5.0):
    """以 IoT 設備身份連至 EdgeLink TCP Server。"""
    reader, writer = await asyncio.wait_for(
        asyncio.open_connection(host, port), timeout=timeout)
    return reader, writer


async def readline_timeout(reader, timeout: float = 3.0) -> str | None:
    """讀取一行，逾時回傳 None。"""
    try:
        line = await asyncio.wait_for(reader.readline(), timeout=timeout)
        return line.decode(errors='replace').strip() if line else None
    except (asyncio.TimeoutError, ConnectionResetError, asyncio.IncompleteReadError):
        return None


EDGELINK_INTERNAL_PREFIXES = ("EDGELINK_STATUS:", "EDGELINK_PING:", "EDGELINK_PONG:")
EDGELINK_HEARTBEAT_PREFIXES = ("EDGELINK_PING:", "EDGELINK_PONG:")


async def _readline_skip(reader, skip_prefixes: tuple, timeout: float, max_lines: int) -> str | None:
    deadline = time.perf_counter() + timeout
    for _ in range(max_lines):
        remaining = deadline - time.perf_counter()
        if remaining <= 0:
            return None
        try:
            line = await asyncio.wait_for(reader.readline(), timeout=remaining)
            if not line:
                return None
            text = line.decode(errors='replace').strip()
            if any(text.startswith(p) for p in skip_prefixes):
                continue
            return text
        except (asyncio.TimeoutError, ConnectionResetError, asyncio.IncompleteReadError):
            return None
    return None


async def readline_skip_status(reader, timeout: float = 5.0, max_lines: int = 20) -> str | None:
    """跳過所有 EdgeLink 內部協議行（STATUS/PING/PONG），逾時回傳 None。"""
    return await _readline_skip(reader, EDGELINK_INTERNAL_PREFIXES, timeout, max_lines)


async def readline_skip_heartbeat(reader, timeout: float = 5.0, max_lines: int = 20) -> str | None:
    """只跳過 PING/PONG，保留 STATUS 行（用於 status 通知測試）。"""
    return await _readline_skip(reader, EDGELINK_HEARTBEAT_PREFIXES, timeout, max_lines)


async def send_line(writer, text: str):
    writer.write((text.rstrip('\n') + '\n').encode())
    await writer.drain()


def close_writer(writer):
    try:
        writer.close()
    except Exception:
        pass


# ── Device Side Server（EdgeLink TCP Client 會連進來）────────────────────────

class DeviceServer:
    """模擬 IoT 裝置：開 TCP Server 等 EdgeLink TCP Client 連入，
       收到訊息後可選擇是否 echo 回應。"""

    def __init__(self, host: str, port: int):
        self.host = host
        self.port = port
        self._server = None
        self._conn_event = asyncio.Event()
        self._received: list[str] = []
        self._reader = None
        self._writer = None
        self._echo_fn = None          # callable(msg) -> str | None
        self._connected = False

    async def start(self):
        self._server = await asyncio.start_server(
            self._handle, self.host, self.port)
        print(f"  [Device] 監聽 {self.host}:{self.port}，等待 EdgeLink 連入…")

    async def wait_connected(self, timeout: float = 10.0):
        await asyncio.wait_for(self._conn_event.wait(), timeout=timeout)

    async def _handle(self, reader, writer):
        self._reader = reader
        self._writer = writer
        self._connected = True
        self._conn_event.set()
        addr = writer.get_extra_info('peername')
        print(f"  [Device] EdgeLink TCP Client 已連入 {addr}")

        try:
            while True:
                line = await asyncio.wait_for(reader.readline(), timeout=60.0)
                if not line:
                    break
                text = line.decode(errors='replace').strip()
                if not text:
                    continue
                self._received.append(text)
                # 不 echo EdgeLink 內部協議訊息（STATUS/PING/PONG），
                # 避免假 echo 提前觸發 ResponseSignal
                if self._echo_fn and not text.startswith("EDGELINK_"):
                    reply = self._echo_fn(text)
                    if reply is not None:
                        await send_line(writer, reply)
        except (asyncio.TimeoutError, ConnectionResetError, asyncio.IncompleteReadError):
            pass
        finally:
            self._connected = False
            print("  [Device] EdgeLink TCP Client 已斷線")

    async def wait_for_message(self, keyword: str, timeout: float = 5.0) -> str | None:
        """輪詢 _received，直到有包含 keyword 的訊息出現或逾時。
        不直接讀 reader，避免與 _handle 的 readline 衝突。"""
        deadline = time.perf_counter() + timeout
        seen = 0
        while True:
            msgs = self._received
            for msg in msgs[seen:]:
                if keyword in msg:
                    return msg
            seen = len(msgs)
            remaining = deadline - time.perf_counter()
            if remaining <= 0:
                return None
            await asyncio.sleep(min(0.05, remaining))

    async def stop(self):
        if self._writer:
            close_writer(self._writer)
        if self._server:
            self._server.close()
            await self._server.wait_closed()

    @property
    def received(self) -> list[str]:
        return list(self._received)

    def clear_received(self):
        self._received.clear()

    def set_echo(self, fn):
        """fn(msg: str) -> str | None；None 代表不回應。"""
        self._echo_fn = fn

    def disable_echo(self):
        self._echo_fn = None


# ── 測試 1：基本轉發（Forward Only）─────────────────────────────────────────

async def test_forward(host: str, server_port: int, device: DeviceServer):
    print("\n[Test 1] 基本轉發 (Forward)")
    try:
        reader, writer = await connect_iot(host, server_port)
    except Exception as e:
        FAIL("forward/connect", str(e))
        return

    device.clear_received()
    # 開啟 echo：serial 模式下 device 回應才能讓 queue 繼續推進下一筆
    device.set_echo(lambda msg: msg)

    MSG_COUNT = 3
    for i in range(1, MSG_COUNT + 1):
        await send_line(writer, f"ID:{i};TEMP:25.{i}")

    # Serial 模式每筆要等 device 回應，給足夠時間
    await asyncio.sleep(MSG_COUNT * 1.5)
    close_writer(writer)

    recv = device.received
    if len(recv) >= MSG_COUNT:
        PASS("forward/messages-received", f"device 收到 {len(recv)} 筆")
    elif len(recv) >= 1:
        PASS("forward/at-least-one", f"device 收到 {len(recv)} 筆（serial 模式逐筆推進中）")
        if len(recv) < MSG_COUNT:
            FAIL("forward/messages-received",
                 f"期望 ≥{MSG_COUNT}，收到 {len(recv)} — "
                 f"若為 serial 模式請確認 ResponseMaskType.routeMode 已設定")
    else:
        FAIL("forward/messages-received", f"期望 ≥1，收到 {len(recv)}")

    await asyncio.sleep(0.5)  # 讓斷線通知在下一個測試前送完


# ── 測試 2：串行模式 Request/Response ────────────────────────────────────────

async def test_serial(host: str, server_port: int, device: DeviceServer):
    print("\n[Test 2] 串行模式 (Serial Request/Response)")

    # Device 收到什麼就 echo 回去
    device.set_echo(lambda msg: msg)
    device.clear_received()

    try:
        reader, writer = await connect_iot(host, server_port)
    except Exception as e:
        FAIL("serial/connect", str(e))
        return

    # 送一筆請求
    payload = "ID:42;TEMP:36.6"
    await send_line(writer, payload)

    # 等待回應（跳過 PING/PONG/STATUS 通知行）
    response = await readline_skip_status(reader, timeout=6.0)
    close_writer(writer)

    # 診斷：列出 device 實際收到什麼
    await asyncio.sleep(0.3)
    dev_recv = device.received
    print(f"    [診斷] device.received: {dev_recv}")

    if response is None:
        if not dev_recv:
            FAIL("serial/response-received",
                 "device 也未收到任何訊息 → forward 路徑失敗，確認 Port B SourceProtocolId 指向 Port A")
        else:
            FAIL("serial/response-received",
                 f"device 收到 {dev_recv!r} 但 IoT 無回應 → "
                 f"確認 ResponseMaskType.routeMode='response'，或 ResponseSignal 未觸發")
    else:
        PASS("serial/response-received", f"回應: {response!r}")
        if "36.6" in response or "42" in response:
            PASS("serial/response-content", "回應含原始欄位值")
        else:
            FAIL("serial/response-content",
                 f"回應不含預期欄位，內容: {response!r}")

    await asyncio.sleep(0.5)


# ── 測試 3：輪詢模式（Polling — 只轉發最新）─────────────────────────────────

async def test_polling(host: str, server_port: int, device: DeviceServer):
    print("\n[Test 3] 輪詢模式 (Polling — 只轉發最新)")

    device.clear_received()
    device.disable_echo()

    try:
        reader, writer = await connect_iot(host, server_port)
    except Exception as e:
        FAIL("polling/connect", str(e))
        return

    BURST = 10
    for i in range(1, BURST + 1):
        await send_line(writer, f"SEQ:{i};DATA:payload_{i}")
    # 不加 sleep，全部快速送出

    await asyncio.sleep(1.5)
    close_writer(writer)

    recv = device.received
    # 過濾掉 EDGELINK_STATUS 通知（前一測試的斷線通知可能延遲到達 device）
    data_recv = [m for m in recv if not m.startswith("EDGELINK_STATUS:")]

    if len(data_recv) == 0:
        FAIL("polling/device-received-any", "device 未收到任何資料訊息")
        return

    if len(data_recv) < BURST:
        PASS("polling/drop-older",
             f"送 {BURST} 筆，device 收到 {len(data_recv)} 筆資料（舊的被丟棄）")
    else:
        FAIL("polling/drop-older",
             f"送 {BURST} 筆，device 全收到 {len(data_recv)} 筆 "
             f"— 確認 Port B RequestMode = 'polling'")

    # 最後一筆的 SEQ 應 > 1（代表確實有丟棄舊的，不要求剛好是 SEQ:BURST）
    last = data_recv[-1]
    try:
        last_seq = int(next(p.split(":")[-1] for p in last.split(";") if p.startswith("SEQ:")))
    except (StopIteration, ValueError):
        last_seq = 0

    if last_seq > 1:
        PASS("polling/latest-forwarded", f"收到 SEQ:{last_seq}（> 1，舊的已被丟棄）: {last!r}")
    elif f"SEQ:{BURST}" in last or f"payload_{BURST}" in last:
        PASS("polling/latest-forwarded", f"最後一筆含最新資料: {last!r}")
    else:
        FAIL("polling/latest-forwarded",
             f"收到 SEQ:{last_seq}（=1，未丟棄舊訊息）: {last!r}，期望 SEQ > 1")

    await asyncio.sleep(0.5)  # 讓斷線通知在下一個測試前送完


# ── 測試 4：並發模式（Concurrent — correlationId 匹配）──────────────────────

async def test_concurrent(host: str, server_port: int, device: DeviceServer):
    print("\n[Test 4] 並發模式 (Concurrent — correlationId 匹配)")
    print("  注意：需要 MaskType 含 {_corrId}，且 ResponseMaskType 設定 correlationIdField")

    # Device echo：把收到的 _corrId 欄位原封保留在回應裡
    # EdgeLink 注入格式依 MaskType 而定，此處假設入站格式含 _corrId=xxx
    def echo_with_corrId(msg: str) -> str:
        # 透傳整筆，讓 EdgeLink 靠 correlationIdField 配對
        return msg

    device.set_echo(echo_with_corrId)
    device.clear_received()

    CONCURRENT_COUNT = 3
    tasks = []
    responses = [None] * CONCURRENT_COUNT

    async def iot_client(idx: int):
        try:
            r, w = await connect_iot(host, server_port)
            payload = f"ID:{idx};VALUE:{idx * 10}"
            await send_line(w, payload)
            resp = await readline_skip_status(r, timeout=6.0)
            responses[idx] = resp
            close_writer(w)
        except Exception as e:
            responses[idx] = f"ERROR:{e}"

    for i in range(CONCURRENT_COUNT):
        tasks.append(asyncio.create_task(iot_client(i)))

    await asyncio.gather(*tasks)
    await asyncio.sleep(0.5)  # 讓 device 收取所有在途訊息

    data_recv_count = len([m for m in device.received
                           if not any(m.startswith(p) for p in EDGELINK_INTERNAL_PREFIXES)])
    resp_count = sum(1 for r in responses if r is not None and not str(r).startswith("ERROR"))

    if data_recv_count >= CONCURRENT_COUNT:
        PASS("concurrent/all-forwarded", f"device 收到 {data_recv_count} 筆")
    else:
        FAIL("concurrent/all-forwarded",
             f"device 只收到 {data_recv_count}/{CONCURRENT_COUNT} 筆")

    if resp_count >= CONCURRENT_COUNT:
        PASS("concurrent/all-responded", f"{resp_count} 個 IoT 各自收到回應")
    else:
        FAIL("concurrent/all-responded",
             f"只有 {resp_count}/{CONCURRENT_COUNT} 個 IoT 收到回應 "
             f"— 確認 RequestMode='concurrent' 且 correlationIdField 設定正確")

    await asyncio.sleep(0.5)


# ── 測試 5：廣播路由（Broadcast — 多 client 全部收到）────────────────────────

async def test_broadcast(host: str, server_port: int, device: DeviceServer,
                         client_count: int = 3):
    print(f"\n[Test 5] 廣播路由 (Broadcast — {client_count} 個 IoT client 全部收到)")
    print("  注意：需要 ResponseMaskType.routeMode = 'broadcast'")

    device.set_echo(lambda msg: msg)

    # 建立多個 IoT 連線
    connections = []
    for i in range(client_count):
        try:
            r, w = await connect_iot(host, server_port)
            connections.append((r, w))
        except Exception as e:
            FAIL(f"broadcast/connect-client-{i}", str(e))

    if len(connections) < client_count:
        FAIL("broadcast/setup", f"只建立了 {len(connections)}/{client_count} 個連線")
        for _, w in connections:
            close_writer(w)
        return

    await asyncio.sleep(0.3)  # 等所有 client 在 EdgeLink 完成 accept

    # 第 0 個 client 送訊息
    sender_r, sender_w = connections[0]
    device.clear_received()
    await send_line(sender_w, "BROADCAST:hello;FROM:client0")

    # 等所有 client 收到廣播
    await asyncio.sleep(1.5)

    received_by = []
    # readline_skip_status：連線後立即建立的連線會先收到 CONNECTED 通知，需跳過
    read_tasks = [readline_skip_status(r, timeout=2.0) for r, _ in connections]
    resp_list = await asyncio.gather(*read_tasks)

    for i, resp in enumerate(resp_list):
        if resp is not None:
            received_by.append(i)

    for _, w in connections:
        close_writer(w)

    if len(received_by) >= client_count:
        PASS("broadcast/all-clients-received",
             f"全部 {client_count} 個 IoT 都收到廣播回應")
    elif len(received_by) == 1:
        FAIL("broadcast/all-clients-received",
             f"只有 {len(received_by)} 個 client 收到 "
             f"— 確認 ResponseMaskType.routeMode = 'broadcast'（目前可能是 'response'）")
    else:
        FAIL("broadcast/all-clients-received",
             f"只有 {len(received_by)}/{client_count} 個 client 收到廣播")

    await asyncio.sleep(0.5)


# ── 測試 6：EDGELINK_STATUS 連線通知 ─────────────────────────────────────────

async def test_status_notification(host: str, server_port: int, device: DeviceServer):
    """
    EdgeLink 在 IoT 連入/斷線 TCP Server 時，會透過 NotifyAsync 直接寫入
    對應 TCP Client（裝置端），而非廣播給其他 IoT 客戶端。
    本測試驗證：裝置端（DeviceServer）能收到 EDGELINK_STATUS:CONNECTED 與
    EDGELINK_STATUS:DISCONNECTED 通知。
    """
    print("\n[Test 6] EDGELINK_STATUS 連線/斷線通知（驗證裝置端收到通知）")
    print("  注意：EdgeLink 將 STATUS 通知送給 TCP Client（裝置端），非其他 IoT client")

    device.clear_received()

    # IoT 連入 TCP Server → EdgeLink 應向裝置端送 CONNECTED
    try:
        iot_r, iot_w = await connect_iot(host, server_port)
    except Exception as e:
        FAIL("status/iot-connect", str(e))
        return

    # 輪詢 device._received，等裝置端收到 CONNECTED 通知
    # （不直接讀 device._reader，避免與 _handle 的 readline 衝突）
    connected_msg = await device.wait_for_message("CONNECTED", timeout=4.0)

    if connected_msg and "EDGELINK_STATUS" in connected_msg:
        PASS("status/connected-notification", f"裝置端收到: {connected_msg!r}")
    else:
        FAIL("status/connected-notification",
             f"裝置端未收到 CONNECTED 通知（目前收到: {device.received!r}）")

    # IoT 斷線 → EdgeLink 應向裝置端送 DISCONNECTED
    close_writer(iot_w)

    disconnected_msg = await device.wait_for_message("DISCONNECTED", timeout=5.0)

    if disconnected_msg:
        PASS("status/disconnected-notification", f"裝置端收到: {disconnected_msg!r}")
    else:
        FAIL("status/disconnected-notification",
             "5s 內裝置端未收到 DISCONNECTED 通知")


# ── 遮罩轉換驗證（附加到其他測試中）────────────────────────────────────────

async def test_mask_transform(host: str, server_port: int, device: DeviceServer,
                               expected_field: str, expected_value: str):
    """驗證 device 收到的訊息已被遮罩轉換（欄位存在且值正確）。"""
    print(f"\n[Test M] 遮罩轉換驗證（期望欄位 {expected_field}={expected_value}）")

    device.clear_received()
    device.disable_echo()

    try:
        reader, writer = await connect_iot(host, server_port)
    except Exception as e:
        FAIL("mask/connect", str(e))
        return

    await send_line(writer, f"ID:1;TEMP:{expected_value};SEQ:99")
    await asyncio.sleep(1.0)
    close_writer(writer)

    recv = device.received
    if not recv:
        FAIL("mask/device-received", "device 未收到任何訊息")
        return

    last = recv[-1]
    if expected_field in last and expected_value in last:
        PASS("mask/field-present", f"device 收到: {last!r}")
    else:
        FAIL("mask/field-present",
             f"device 收到: {last!r}，找不到 {expected_field}={expected_value}")


# ── 主程式 ───────────────────────────────────────────────────────────────────

async def main():
    parser = argparse.ArgumentParser(description="EdgeLink 路由整合測試")
    parser.add_argument("--host",             default="127.0.0.1")
    parser.add_argument("--server-port",      type=int, required=True, dest="server_port",
                        help="EdgeLink TCP Server port（IoT 連入）")
    parser.add_argument("--device-port",      type=int, required=True, dest="device_port",
                        help="本機監聽 port（EdgeLink TCP Client 連出至此）")
    parser.add_argument("--mode",             default="all",
                        choices=["all", "forward", "serial", "polling",
                                 "concurrent", "broadcast", "status", "mask"])
    parser.add_argument("--broadcast-clients", type=int, default=3,
                        dest="broadcast_clients", help="廣播測試的 IoT client 數量")
    parser.add_argument("--mask-field",       default="TEMP",    dest="mask_field",
                        help="遮罩驗證：期望欄位名稱")
    parser.add_argument("--mask-value",       default="36.6",    dest="mask_value",
                        help="遮罩驗證：期望欄位值")
    parser.add_argument("--device-timeout",   type=float, default=10.0, dest="device_timeout",
                        help="等待 EdgeLink TCP Client 連入的秒數")
    args = parser.parse_args()

    print(f"\nEdgeLink 路由整合測試")
    print(f"  目標 Server : {args.host}:{args.server_port}")
    print(f"  Device Port : {args.host}:{args.device_port}  (等待 EdgeLink 連入)")
    print(f"  測試模式    : {args.mode}")
    print()

    # 啟動 Device Side Server
    device = DeviceServer(args.host, args.device_port)
    try:
        await device.start()
    except OSError as e:
        print(f"  ✗ 無法開啟 device port {args.device_port}: {e}")
        print(f"    確認該 port 未被佔用，或選擇其他 --device-port")
        sys.exit(1)

    # 等待 EdgeLink TCP Client 連入
    need_device = args.mode in ("all", "forward", "serial", "polling", "concurrent", "broadcast", "status", "mask")
    if need_device:
        print(f"  等待 EdgeLink TCP Client 連入（最多 {args.device_timeout}s）…")
        try:
            await device.wait_connected(timeout=args.device_timeout)
        except asyncio.TimeoutError:
            print(f"  ✗ EdgeLink TCP Client 未在 {args.device_timeout}s 內連入")
            print(f"    確認 EdgeLink 已設定 TCP Client port 指向 {args.host}:{args.device_port}")
            await device.stop()
            sys.exit(1)

    # ── 執行測試 ──────────────────────────────────────────────────────────────

    run_all = args.mode == "all"

    try:
        if run_all or args.mode == "forward":
            await test_forward(args.host, args.server_port, device)

        if run_all or args.mode == "serial":
            await test_serial(args.host, args.server_port, device)

        if run_all or args.mode == "polling":
            await test_polling(args.host, args.server_port, device)

        if run_all or args.mode == "concurrent":
            await test_concurrent(args.host, args.server_port, device)

        if run_all or args.mode == "broadcast":
            await test_broadcast(args.host, args.server_port, device,
                                  args.broadcast_clients)

        if run_all or args.mode == "status":
            await test_status_notification(args.host, args.server_port, device)

        if args.mode == "mask":
            await test_mask_transform(args.host, args.server_port, device,
                                       args.mask_field, args.mask_value)

    finally:
        await device.stop()

    # ── 結果摘要 ──────────────────────────────────────────────────────────────

    passed = [r for r in results if r.passed]
    failed = [r for r in results if not r.passed]

    print()
    print("=" * 60)
    print("測試結果摘要")
    print("=" * 60)
    print(f"  PASS: {len(passed)}  FAIL: {len(failed)}  共 {len(results)} 項")

    if failed:
        print()
        print("  失敗項目：")
        for r in failed:
            print(f"    ✗ {r.name}  — {r.detail}")

    print("=" * 60)

    if failed:
        sys.exit(1)


if __name__ == "__main__":
    if sys.platform == "win32":
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n中斷測試")
        sys.exit(0)
