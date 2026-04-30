"""
EdgeLink Example — Data Receiver
=================================
Listens for processed data forwarded by EdgeLink.
EdgeLink connects to this receiver after transforming
the raw sensor data through the SensorMask.

Expected output format (after SensorMask):
  Temperature: 24.5C | Humidity: 62.3%

Usage:
  python receiver.py
  python receiver.py --port 9002
"""

import socket
import argparse
import sys
from datetime import datetime


def main():
    parser = argparse.ArgumentParser(description="EdgeLink data receiver")
    parser.add_argument("--host", default="127.0.0.1", help="Listen host (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=9002, help="Listen port (default: 9002)")
    args = parser.parse_args()

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((args.host, args.port))
    server.listen(5)

    print(f"\nData Receiver — listening on {args.host}:{args.port}")
    print(f"  Waiting for EdgeLink to connect...  (Ctrl+C to stop)\n")

    try:
        while True:
            conn, addr = server.accept()
            print(f"✓ EdgeLink connected from {addr[0]}:{addr[1]}\n")

            with conn:
                buffer = ""
                while True:
                    try:
                        chunk = conn.recv(1024).decode()
                        if not chunk:
                            print("  Connection closed by EdgeLink.\n")
                            break
                        buffer += chunk
                        while "\n" in buffer:
                            line, buffer = buffer.split("\n", 1)
                            line = line.strip()
                            if line:
                                ts = datetime.now().strftime("%H:%M:%S")
                                print(f"  [{ts}] {line}")
                    except (ConnectionResetError, OSError):
                        print("  Connection lost.\n")
                        break

    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        server.close()


if __name__ == "__main__":
    main()
