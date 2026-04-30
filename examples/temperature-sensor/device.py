"""
EdgeLink Example — Temperature Sensor Simulator
================================================
Simulates an IoT temperature/humidity sensor that sends data
to EdgeLink every second.

Protocol format:
  TEMP:<value>;HUM:<value>

Example:
  TEMP:24.5;HUM:62.3

Usage:
  python device.py
  python device.py --host 192.168.0.10 --port 9001 --interval 2
"""

import socket
import time
import random
import argparse
import sys


def simulate_reading():
    temp = round(20.0 + random.uniform(-2.0, 10.0), 1)
    hum  = round(50.0 + random.uniform(-10.0, 20.0), 1)
    return temp, hum


def main():
    parser = argparse.ArgumentParser(description="Simulated IoT temperature/humidity sensor")
    parser.add_argument("--host",     default="127.0.0.1", help="EdgeLink TCP Server host (default: 127.0.0.1)")
    parser.add_argument("--port",     type=int, default=9001, help="EdgeLink TCP Server port (default: 9001)")
    parser.add_argument("--interval", type=float, default=1.0, help="Send interval in seconds (default: 1.0)")
    args = parser.parse_args()

    print(f"\nTemperature Sensor — connecting to {args.host}:{args.port}...")

    while True:
        try:
            with socket.create_connection((args.host, args.port), timeout=5) as sock:
                print(f"✓ Connected to EdgeLink at {args.host}:{args.port}")
                print(f"  Sending readings every {args.interval}s  (Ctrl+C to stop)\n")

                while True:
                    temp, hum = simulate_reading()
                    message = f"TEMP:{temp};HUM:{hum}\n"
                    sock.sendall(message.encode())
                    print(f"  → Sent: {message.strip()}")
                    time.sleep(args.interval)

        except ConnectionRefusedError:
            print(f"✗ Connection refused — is EdgeLink running and port 9001 open?")
            print(f"  Retrying in 3 seconds...")
            time.sleep(3)
        except (BrokenPipeError, ConnectionResetError, OSError):
            print(f"  Connection lost. Reconnecting in 3 seconds...")
            time.sleep(3)
        except KeyboardInterrupt:
            print("\nStopped.")
            sys.exit(0)


if __name__ == "__main__":
    main()
