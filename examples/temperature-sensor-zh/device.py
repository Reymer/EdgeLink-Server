"""
EdgeLink 範例 — 溫度感測器模擬器
==================================
模擬一個每秒傳送溫度與濕度資料的 IoT 感測器。

資料格式：
  TEMP:<數值>;HUM:<數值>

範例：
  TEMP:24.5;HUM:62.3

用法：
  python device.py
  python device.py --host 192.168.0.10 --port 9001 --interval 2
"""

import socket
import time
import random
import argparse
import sys


def 模擬讀值():
    temp = round(20.0 + random.uniform(-2.0, 10.0), 1)
    hum  = round(50.0 + random.uniform(-10.0, 20.0), 1)
    return temp, hum


def main():
    parser = argparse.ArgumentParser(description="IoT 溫濕度感測器模擬器")
    parser.add_argument("--host",     default="127.0.0.1", help="EdgeLink TCP Server 位址（預設：127.0.0.1）")
    parser.add_argument("--port",     type=int, default=9001, help="EdgeLink TCP Server 埠號（預設：9001）")
    parser.add_argument("--interval", type=float, default=1.0, help="傳送間隔秒數（預設：1.0）")
    args = parser.parse_args()

    print(f"\n溫度感測器 — 連線至 {args.host}:{args.port}...")

    while True:
        try:
            with socket.create_connection((args.host, args.port), timeout=5) as sock:
                sock.setsockopt(socket.SOL_SOCKET, socket.SO_KEEPALIVE, 1)
                print(f"✓ 已連線至 EdgeLink {args.host}:{args.port}")
                print(f"  每 {args.interval} 秒傳送一次資料（Ctrl+C 停止）\n")

                while True:
                    temp, hum = 模擬讀值()
                    message = f"TEMP:{temp};HUM:{hum}\n"
                    sock.sendall(message.encode())
                    print(f"  → 已送出：{message.strip()}")
                    time.sleep(args.interval)

        except ConnectionRefusedError:
            print(f"✗ 連線被拒絕 — EdgeLink 是否已啟動且 Port 9001 已開啟？")
            print(f"  3 秒後重試...")
            time.sleep(3)
        except (BrokenPipeError, ConnectionResetError, OSError):
            print(f"  連線中斷，3 秒後重新連線...")
            time.sleep(3)
        except KeyboardInterrupt:
            print("\n已停止。")
            sys.exit(0)


if __name__ == "__main__":
    main()
