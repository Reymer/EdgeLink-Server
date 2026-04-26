"""
EdgeLink 全模式整合測試執行器

依序對各獨立 Port 組執行 routing_integration_test.py，
每種模式用對應的 Server/Device port 對，結果彙整於最後。

Port 對應（需先執行 setup_test_ports.py）：
  forward / serial   → server=9998, device=7776
  polling            → server=9997, device=7775
  concurrent         → server=9999, device=7777
  broadcast          → server=9996, device=7774
  status             → server=9999（不需 device）

用法：
  python tools/run_all_tests.py [--host 127.0.0.1]
  python tools/run_all_tests.py --skip broadcast    # 跳過指定模式
"""

import argparse
import subprocess
import sys
import time
from pathlib import Path

if sys.stdout.encoding and sys.stdout.encoding.lower() != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

SCRIPT = Path(__file__).parent / "routing_integration_test.py"

# (mode, server_port, device_port)  — device_port=None 表示該模式不需要 device
SUITES = [
    ("forward",    9998, 7776),
    ("serial",     9998, 7776),
    ("polling",    9997, 7775),
    ("concurrent", 9999, 7777),
    ("broadcast",  9996, 7774),
    ("status",     9999, 7777),
]


def run_suite(host: str, mode: str, server: int, device: int | None,
              device_timeout: float = 10.0) -> tuple[str, bool]:
    """執行單一模式測試，即時印出輸出，回傳 (mode, passed)。"""
    cmd = [
        sys.executable, str(SCRIPT),
        "--host", host,
        "--server-port", str(server),
        "--device-port", str(device if device else 9000),
        "--mode", mode,
        "--device-timeout", str(device_timeout),
    ]

    print(f"\n{'='*60}")
    print(f"  執行模式: {mode}  (server={server}, device={device or 'N/A'})")
    print(f"{'='*60}")
    sys.stdout.flush()

    result = subprocess.run(cmd, text=True, errors='replace')
    return mode, result.returncode == 0


def main():
    parser = argparse.ArgumentParser(description="EdgeLink 全模式整合測試")
    parser.add_argument("--host",   default="127.0.0.1")
    parser.add_argument("--skip",   nargs="*", default=[],
                        help="跳過的模式（例如 --skip broadcast status）")
    parser.add_argument("--only",   nargs="*", default=None,
                        help="只執行指定模式（例如 --only serial concurrent）")
    parser.add_argument("--device-timeout", type=float, default=10.0, dest="device_timeout",
                        help="等待 EdgeLink TCP Client 連入的秒數（預設 10）")
    args = parser.parse_args()

    suites = SUITES
    if args.only:
        suites = [(m, s, d) for m, s, d in suites if m in args.only]
    if args.skip:
        suites = [(m, s, d) for m, s, d in suites if m not in args.skip]

    if not suites:
        print("沒有可執行的測試")
        sys.exit(0)

    print(f"\nEdgeLink 全模式整合測試")
    print(f"  Host  : {args.host}")
    print(f"  模式  : {', '.join(m for m, *_ in suites)}")

    results: list[tuple[str, bool]] = []
    t_start = time.perf_counter()

    for mode, server, device in suites:
        mode_name, passed = run_suite(args.host, mode, server, device, args.device_timeout)
        results.append((mode_name, passed))
        time.sleep(1.0)  # 讓前一輪 port 完全釋放

    elapsed = time.perf_counter() - t_start

    # ── 彙整 ──────────────────────────────────────────────────────────────────
    passed_count = sum(1 for _, p in results if p)
    failed = [(m, p) for m, p in results if not p]

    print()
    print("=" * 60)
    print("全模式測試結果彙整")
    print("=" * 60)
    print(f"  PASS: {passed_count}  FAIL: {len(failed)}  共 {len(results)} 個模式"
          f"  耗時 {elapsed:.1f}s")

    if failed:
        print()
        print("  失敗模式：")
        for m, _ in failed:
            print(f"    ✗ {m}")
    else:
        print()
        print("  ✓ 全部通過")

    print("=" * 60)
    sys.exit(0 if not failed else 1)


if __name__ == "__main__":
    main()
