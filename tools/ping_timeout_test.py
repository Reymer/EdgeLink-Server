"""
TcpClientMetrics PING/PONG 行為整合測試

模擬兩種設備行為：
  1. 正常設備：收到 PING 就回 PONG（不應觸發掉線）
  2. 掉電設備：收到 PING 不回應（應在 ~15s 後被 EdgeLink 強制斷線）

用法：
  先在 EdgeLink 開啟一個 TCP Server port（例如 9000），然後執行：

  python tools/ping_timeout_test.py --port 9000 --mode pong    # 正常設備模式
  python tools/ping_timeout_test.py --port 9000 --mode silent  # 掉電設備模式
  python tools/ping_timeout_test.py --port 9000 --mode all     # 兩種模式都跑
"""

import asyncio
import argparse
import sys
import time

PING_PREFIX = "EDGELINK_PING:"
PONG_PREFIX = "EDGELINK_PONG:"


async def device_pong(host: str, port: int, duration: float):
    """正常設備：收 PING 回 PONG，預期連線維持整段時間。"""
    print(f"[pong-device] 連線 {host}:{port}")
    reader, writer = await asyncio.open_connection(host, port)
    start = time.perf_counter()
    pong_count = 0
    buf = ""

    try:
        while time.perf_counter() - start < duration:
            try:
                data = await asyncio.wait_for(reader.read(256), timeout=1.0)
            except asyncio.TimeoutError:
                continue
            if not data:
                print("[pong-device] 連線被關閉（非預期）")
                return

            buf += data.decode(errors="replace")
            while "\n" in buf:
                line, buf = buf.split("\n", 1)
                line = line.strip()
                if line.startswith(PING_PREFIX):
                    pong = line.replace(PING_PREFIX, PONG_PREFIX) + "\n"
                    writer.write(pong.encode())
                    await writer.drain()
                    pong_count += 1
                    print(f"  [pong-device] PONG #{pong_count}")

        elapsed = time.perf_counter() - start
        print(f"[pong-device] 測試完成，{elapsed:.1f}s 內送出 {pong_count} 個 PONG，連線維持正常 ✓")
    except Exception as e:
        print(f"[pong-device] 例外: {e}")
    finally:
        writer.close()


async def device_silent(host: str, port: int, timeout_threshold: float = 20.0):
    """掉電設備：連線後完全不回應，等待 EdgeLink 強制斷線。"""
    print(f"[silent-device] 連線 {host}:{port}，模擬掉電（不回 PONG）...")
    reader, writer = await asyncio.open_connection(host, port)
    start = time.perf_counter()
    ping_count = 0
    buf = ""

    try:
        while time.perf_counter() - start < timeout_threshold:
            try:
                data = await asyncio.wait_for(reader.read(256), timeout=1.0)
            except asyncio.TimeoutError:
                continue

            if not data:
                elapsed = time.perf_counter() - start
                print(f"[silent-device] EdgeLink 在 {elapsed:.1f}s 後強制斷線 ✓")
                print(f"  收到 {ping_count} 個 PING，無回應後被踢出")
                return

            buf += data.decode(errors="replace")
            while "\n" in buf:
                line, buf = buf.split("\n", 1)
                line = line.strip()
                if line.startswith(PING_PREFIX):
                    ping_count += 1
                    elapsed = time.perf_counter() - start
                    print(f"  [silent-device] 收到 PING #{ping_count}（{elapsed:.1f}s）— 不回應")

        elapsed = time.perf_counter() - start
        print(f"[silent-device] ✗ {elapsed:.1f}s 後仍未被強制斷線，PING 超時偵測可能未正常運作")
    except Exception as e:
        elapsed = time.perf_counter() - start
        print(f"[silent-device] 連線在 {elapsed:.1f}s 後中斷（{type(e).__name__}）✓")
    finally:
        try:
            writer.close()
        except Exception:
            pass


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--mode", choices=["pong", "silent", "all"], default="all")
    parser.add_argument("--duration", type=float, default=30.0,
                        help="pong 模式持續秒數（預設 30）")
    args = parser.parse_args()

    if args.mode == "pong":
        await device_pong(args.host, args.port, args.duration)
    elif args.mode == "silent":
        await device_silent(args.host, args.port, timeout_threshold=args.duration)
    else:
        print("=== 測試 1：正常設備（pong 模式）===")
        await device_pong(args.host, args.port, duration=15.0)
        print()
        print("=== 測試 2：掉電設備（silent 模式）===")
        await device_silent(args.host, args.port, timeout_threshold=30.0)


if __name__ == "__main__":
    if sys.platform == "win32":
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n中斷測試")
