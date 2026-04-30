"""
EdgeLink 範例 — 資料接收端
============================
監聽 EdgeLink 轉發過來的處理後資料。
EdgeLink 將原始感測器資料經過 SensorMask 轉換後，
主動連線至本接收端並送出格式化結果。

預期收到的格式（經 SensorMask 處理後）：
  溫度：24.5°C｜濕度：62.3%

用法：
  python receiver.py
  python receiver.py --port 9002
"""

import socket
import argparse
import sys
from datetime import datetime


def main():
    parser = argparse.ArgumentParser(description="EdgeLink 資料接收端")
    parser.add_argument("--host", default="127.0.0.1", help="監聽位址（預設：127.0.0.1）")
    parser.add_argument("--port", type=int, default=9002, help="監聽埠號（預設：9002）")
    args = parser.parse_args()

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((args.host, args.port))
    server.listen(5)

    print(f"\n資料接收端 — 監聽 {args.host}:{args.port}")
    print(f"  等待 EdgeLink 連線...（Ctrl+C 停止）\n")

    try:
        while True:
            conn, addr = server.accept()
            print(f"✓ EdgeLink 已連線（來自 {addr[0]}:{addr[1]}）\n")

            with conn:
                buffer = ""
                while True:
                    try:
                        chunk = conn.recv(1024).decode()
                        if not chunk:
                            print("  EdgeLink 已關閉連線。\n")
                            break
                        buffer += chunk
                        while "\n" in buffer:
                            line, buffer = buffer.split("\n", 1)
                            line = line.strip()
                            if line:
                                ts = datetime.now().strftime("%H:%M:%S")
                                print(f"  [{ts}] {line}")
                    except (ConnectionResetError, OSError):
                        print("  連線中斷。\n")
                        break

    except KeyboardInterrupt:
        print("\n已停止。")
    finally:
        server.close()


if __name__ == "__main__":
    main()
