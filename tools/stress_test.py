"""
EdgeLink Server TCP 高併發壓力測試

用法：
    python tools/stress_test.py --tcp-port 8888 --clients 3 --interval 1 --duration 10

參數：
    --host       伺服器 IP (預設 127.0.0.1)
    --tcp-port   TCP Server 監聽的 Port（必填）
    --clients    同時連線的 client 數量（預設 3）
    --interval   每筆訊息間隔，毫秒（預設 1）
    --duration   測試持續秒數（預設 10）
    --web-port   Web UI port，用於測量回應性（預設 8181，設 0 跳過）
    --msg        自訂訊息模板，{id} 為 client 編號，{seq} 為序號
                 預設：ID:{id};TEMP:25.3;SEQ:{seq}
"""

import asyncio
import argparse
import time
import sys
import socket
import http.client
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field

# Windows console UTF-8 輸出
if sys.stdout.encoding and sys.stdout.encoding.lower() != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')


# ── 統計資料 ─────────────────────────────────────────────────────────────────

@dataclass
class Stats:
    sent:          int   = 0
    errors:        int   = 0
    dropped:       int   = 0      # 送出但收到連線錯誤
    web_ok:        int   = 0
    web_fail:      int   = 0
    web_latency_ms: list = field(default_factory=list)


# ── TCP Client 協程 ──────────────────────────────────────────────────────────

async def run_client(host: str, port: int, interval_s: float,
                     msg_template: str, client_id: int,
                     stats: Stats, stop: asyncio.Event):
    while not stop.is_set():
        try:
            reader, writer = await asyncio.wait_for(
                asyncio.open_connection(host, port), timeout=5.0)
        except Exception as e:
            print(f"  [Client {client_id}] 連線失敗: {e}，2 秒後重試…")
            try:
                await asyncio.wait_for(stop.wait(), timeout=2.0)
            except asyncio.TimeoutError:
                pass
            continue

        print(f"  [Client {client_id}] 已連線 {host}:{port}")
        seq = 0
        try:
            while not stop.is_set():
                seq += 1
                msg = msg_template.format(id=client_id, seq=seq) + "\n"
                try:
                    writer.write(msg.encode())
                    await writer.drain()
                    stats.sent += 1
                except Exception:
                    stats.dropped += 1
                    break

                if interval_s > 0:
                    await asyncio.sleep(interval_s)
        finally:
            try:
                writer.close()
                await writer.wait_closed()
            except Exception:
                pass
            print(f"  [Client {client_id}] 已斷線（sent={seq}）")


# ── Web API 輪詢協程（http.client 在執行緒中執行，繞過 Windows ProactorEventLoop 限制）─

_http_executor = ThreadPoolExecutor(max_workers=4)


_web_addr_cache: tuple[str, str] | None = None   # (connect_addr, host_header)


def _http_get_blocking(host: str, port: int, path: str) -> int:
    """同步 HTTP GET，回傳 status code 或 -1。
    快取成功的位址避免每次重試 3 秒 IPv4 超時；
    首次依序嘗試 IPv4 → IPv6 [::1]。"""
    global _web_addr_cache

    candidates: list[tuple[str, str]]
    if _web_addr_cache:
        candidates = [_web_addr_cache]
    else:
        candidates = [(host, host), ('::1', 'localhost')]

    for connect_addr, host_header in candidates:
        conn = None
        try:
            conn = http.client.HTTPConnection(host_header, port, timeout=3)
            conn.sock = socket.create_connection((connect_addr, port), 0.5)
            conn.sock.settimeout(3)  # 手動設 sock 後需再套用 read timeout
            conn.request("GET", path, headers={"Connection": "close"})
            status = conn.getresponse().status
            _web_addr_cache = (connect_addr, host_header)  # 記住成功位址
            return status
        except Exception:
            pass
        finally:
            if conn:
                try:
                    conn.close()
                except Exception:
                    pass
    return -1


async def poll_web(host: str, web_port: int, stats: Stats, stop: asyncio.Event):
    # 用 "/" (WebUI 首頁) 做健康檢查，不需要登入認證
    while not stop.is_set():
        t0 = time.perf_counter()
        try:
            loop = asyncio.get_event_loop()
            status = await loop.run_in_executor(
                _http_executor, _http_get_blocking, host, web_port, "/")
            lat = (time.perf_counter() - t0) * 1000
            if status in (200, 304):   # 200 OK 或 304 Not Modified 都算正常
                stats.web_ok += 1
                stats.web_latency_ms.append(lat)
            else:
                stats.web_fail += 1
        except Exception:
            stats.web_fail += 1

        try:
            await asyncio.wait_for(stop.wait(), timeout=0.5)
        except asyncio.TimeoutError:
            pass


# ── 進度列印協程 ─────────────────────────────────────────────────────────────

async def print_progress(stats: Stats, duration: float, stop: asyncio.Event):
    t0 = time.perf_counter()
    prev_sent = 0
    while not stop.is_set():
        try:
            await asyncio.wait_for(stop.wait(), timeout=1.0)
        except asyncio.TimeoutError:
            pass
        elapsed = time.perf_counter() - t0
        rate = stats.sent - prev_sent
        prev_sent = stats.sent

        web_avg = (sum(stats.web_latency_ms[-10:]) / len(stats.web_latency_ms[-10:])
                   if stats.web_latency_ms else 0)
        web_status = f"Web 回應 {web_avg:.0f}ms" if stats.web_ok else "Web 未測試"
        if stats.web_fail > 0:
            web_status += f" ({stats.web_fail} 次失敗)"

        print(f"  [{elapsed:5.1f}s] 已送 {stats.sent:,} 筆 | "
              f"速率 {rate:,} msg/s | {web_status}")


# ── 主程式 ───────────────────────────────────────────────────────────────────

async def main():
    parser = argparse.ArgumentParser(description="EdgeLink Server 壓力測試")
    parser.add_argument("--host",      default="127.0.0.1")
    parser.add_argument("--tcp-port",  type=int, required=True, dest="tcp_port")
    parser.add_argument("--clients",   type=int, default=3)
    parser.add_argument("--interval",  type=float, default=1.0,
                        help="訊息間隔（毫秒）")
    parser.add_argument("--duration",  type=float, default=10.0,
                        help="測試持續秒數")
    parser.add_argument("--web-port",  type=int, default=8181, dest="web_port")
    parser.add_argument("--msg",       default="ID:{id};TEMP:25.3;SEQ:{seq}")
    args = parser.parse_args()

    interval_s = args.interval / 1000.0
    stats  = Stats()
    stop   = asyncio.Event()

    print(f"\nEdgeLink Server 壓力測試")
    print(f"  目標   : {args.host}:{args.tcp_port}")
    print(f"  Clients: {args.clients} 個")
    rate_str = f"{1/interval_s:.0f}" if interval_s > 0 else "∞"
    print(f"  間隔   : {args.interval} ms（理論 {rate_str} msg/s/client）")
    print(f"  持續   : {args.duration} 秒")
    print(f"  訊息   : {args.msg}")
    print()

    tasks = []

    # TCP clients
    for i in range(1, args.clients + 1):
        tasks.append(asyncio.create_task(
            run_client(args.host, args.tcp_port, interval_s,
                       args.msg, i, stats, stop)))

    # Web 輪詢
    if args.web_port > 0:
        tasks.append(asyncio.create_task(
            poll_web(args.host, args.web_port, stats, stop)))

    # 進度列印
    tasks.append(asyncio.create_task(print_progress(stats, args.duration, stop)))

    # 等待測試時間到
    await asyncio.sleep(args.duration)
    stop.set()

    # 等所有 task 結束（最多 3 秒）
    await asyncio.wait(tasks, timeout=3.0)
    for t in tasks:
        t.cancel()

    # ── 結果報告 ────────────────────────────────────────────────────────────
    print()
    print("=" * 55)
    print("測試結果")
    print("=" * 55)
    print(f"  總送出訊息 : {stats.sent:,}")
    print(f"  連線錯誤   : {stats.errors}")
    print(f"  中途掉包   : {stats.dropped}")

    if stats.web_latency_ms:
        lats = sorted(stats.web_latency_ms)
        avg  = sum(lats) / len(lats)
        p95  = lats[int(len(lats) * 0.95)]
        p99  = lats[int(len(lats) * 0.99)]
        print()
        print(f"  Web API 回應 ({stats.web_ok} 次成功 / {stats.web_fail} 次失敗)")
        print(f"    平均: {avg:.1f} ms")
        print(f"    P95 : {p95:.1f} ms")
        print(f"    P99 : {p99:.1f} ms")
        if stats.web_fail > 0:
            fail_rate = stats.web_fail / (stats.web_ok + stats.web_fail) * 100
            print(f"  ⚠ Web 失敗率: {fail_rate:.1f}%（Web UI 有問題）")
        elif avg > 500:
            print(f"  ⚠ Web 平均回應 > 500ms，可能受高負載影響")
        else:
            print(f"  ✓ Web UI 在壓力下仍正常回應")
    elif args.web_port > 0:
        print(f"  Web API: 無回應（web_port={args.web_port} 無法連線）")

    print("=" * 55)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n中斷測試")
        sys.exit(0)
