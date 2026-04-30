"""
EdgeLink 範例 — 溫度感測器設定腳本
=====================================
透過 API 自動設定 EdgeLink：
  - 建立解析 TEMP/HUM 欄位的 Mask
  - 建立 TCP Server Port（設備連入）
  - 建立 TCP Client Port（轉發至接收端）

用法：
  python setup.py
  python setup.py --host 192.168.0.10 --port 8080 --password 你的密碼
"""

import http.client
import json
import argparse
import sys


# ── API 客戶端 ────────────────────────────────────────────────────────────────

class EdgeLinkClient:
    def __init__(self, host: str, port: int):
        self.host = host
        self.port = port
        self._cookie = ""

    def _request(self, method: str, path: str, body: dict = None):
        headers = {"Content-Type": "application/json"}
        if self._cookie:
            headers["Cookie"] = self._cookie

        conn = http.client.HTTPConnection(self.host, self.port, timeout=10)
        data = json.dumps(body).encode() if body else b""

        try:
            conn.request(method, path, body=data, headers=headers)
            resp = conn.getresponse()
            status = resp.status
            raw = resp.read().decode()

            sc = resp.getheader("Set-Cookie", "")
            if "edgelink_sid=" in sc and "edgelink_sid=;" not in sc:
                sid = sc.split("edgelink_sid=")[1].split(";")[0]
                self._cookie = f"edgelink_sid={sid}"

            return status, json.loads(raw) if raw else {}
        except ConnectionRefusedError:
            print(f"\n✗ 無法連線到 http://{self.host}:{self.port}")
            print(f"  請確認 EdgeLink Server 已啟動。")
            sys.exit(1)
        finally:
            conn.close()

    def login(self, password: str):
        status, body = self._request("POST", "/api/auth/login", {"password": password})
        if status == 200 and body.get("success"):
            print("✓ 登入成功")
        else:
            print(f"✗ 登入失敗：{body.get('error', '未知錯誤')}")
            sys.exit(1)

    def get(self, path):             return self._request("GET",  path)
    def post(self, path, body=None): return self._request("POST", path, body)
    def put(self, path, body):       return self._request("PUT",  path, body)


# ── 輔助函式 ──────────────────────────────────────────────────────────────────

def ensure_mask(api: EdgeLinkClient, mask_id: str, definition: dict):
    status, _ = api.get(f"/api/masks/{mask_id}")
    if status == 404:
        s, b = api.post("/api/masks", {"maskId": mask_id, "localizationKey": mask_id})
        if s not in (200, 201):
            print(f"  ✗ 建立 Mask '{mask_id}' 失敗：{b}")
            return
        print(f"  + 建立 Mask '{mask_id}'")
    else:
        print(f"  ~ Mask '{mask_id}' 已存在，更新定義...")

    api.put(f"/api/masks/{mask_id}", {**definition, "maskId": mask_id})
    print(f"    定義已套用。")


def get_ports(api: EdgeLinkClient):
    _, body = api.get("/api/ports")
    return body.get("ports", [])


def find_port(ports, protocol, port_number):
    field = "localPort" if "Server" in protocol else "remotePort"
    return next((p for p in ports
                 if p.get("netProtocol") == protocol
                 and p.get(field) == str(port_number)), None)


def create_server_port(api: EdgeLinkClient, port_number: int, name: str) -> str | None:
    ports = get_ports(api)
    existing = find_port(ports, "TCP Server", port_number)
    if existing:
        print(f"  ~ TCP Server :{port_number} 已存在 (id={existing['id'][:8]})")
        return existing["id"]

    api.post("/api/ports", {
        "protocolName": name,
        "netProtocol":  "TCP Server",
        "localPort":    str(port_number),
        "remotePort":   "--",
        "maskType":     "OriginalData",
        "requestMode":  "serial",
    })

    ports = get_ports(api)
    p = find_port(ports, "TCP Server", port_number)
    if p:
        print(f"  + 建立 TCP Server :{port_number} (id={p['id'][:8]})")
        return p["id"]
    return None


def create_client_port(api: EdgeLinkClient, port_number: int, name: str,
                        mask: str, response_mask: str, source_id: str):
    ports = get_ports(api)
    existing = find_port(ports, "TCP Client", port_number)
    if existing:
        print(f"  ~ TCP Client :{port_number} 已存在")
        return

    api.post("/api/ports", {
        "protocolName":     name,
        "netProtocol":      "TCP Client",
        "localPort":        "--",
        "remotePort":       str(port_number),
        "targetIp":         "127.0.0.1",
        "maskType":         mask,
        "responseMaskType": response_mask,
        "requestMode":      "serial",
        "sourceProtocolId": source_id,
    })
    print(f"  + 建立 TCP Client :{port_number}")


# ── 主程式 ────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="EdgeLink 溫度感測器範例設定")
    parser.add_argument("--host",     default="127.0.0.1", help="EdgeLink 主機位址（預設：127.0.0.1）")
    parser.add_argument("--port",     type=int, default=8080, help="EdgeLink HTTP 埠號（預設：8080）")
    parser.add_argument("--password", default="admin", help="Web UI 密碼（預設：admin）")
    args = parser.parse_args()

    api = EdgeLinkClient(args.host, args.port)

    print(f"\nEdgeLink 溫度感測器範例 — 設定腳本")
    print(f"目標：http://{args.host}:{args.port}\n")

    api.login(args.password)

    # ── Mask ──────────────────────────────────────────────────────────────────
    print("\n[Mask]")
    ensure_mask(api, "SensorMask", {
        "localizationKey": "SensorMask",
        "description":     "解析 TEMP/HUM 欄位並格式化輸出",
        "fieldDelimiter":  ";",
        "kvSeparator":     ":",
        "outputTemplate":  "溫度：{TEMP}°C｜濕度：{HUM}%",
        "routeMode":       "broadcast",
    })

    # ── Ports ─────────────────────────────────────────────────────────────────
    print("\n[Port]")

    # TCP Server :9001 — 設備連入
    server_id = create_server_port(api, 9001, "感測器輸入")

    if server_id:
        # TCP Client :9002 — 轉發處理後資料給 receiver.py
        create_client_port(api, 9002, "感測器輸出",
                            mask="SensorMask",
                            response_mask="OriginalData",
                            source_id=server_id)

    print("\n✓ 設定完成！\n")
    print("請在不同終端機分別執行：")
    print("  1.  python receiver.py")
    print("  2.  python device.py")
    print()
    print("資料流向：")
    print("  device.py  →  EdgeLink :9001  →  [SensorMask]  →  EdgeLink :9002  →  receiver.py")


if __name__ == "__main__":
    main()
