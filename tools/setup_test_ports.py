"""
EdgeLink 測試環境完整設定腳本

透過 Web API 建立（或更新）所有測試所需的遮罩與 Port 組合：

  遮罩：
    ConcurrentMask — 並發模式入站遮罩，注入 _corrId
    ResponseRaw    — 單播回應遮罩（routeMode=response）
    BroadcastRaw   — 廣播回應遮罩（routeMode=broadcast）

  Port 組合：
    concurrent pair — TCP Server :9999 + TCP Client :7777
    serial pair     — TCP Server :9998 + TCP Client :7776
    polling pair    — TCP Server :9997 + TCP Client :7775
    broadcast pair  — TCP Server :9996 + TCP Client :7774

用法：
  python tools/setup_test_ports.py [--password <密碼>] [--web-port 8181]
"""

import http.client
import json
import argparse
import sys

if sys.stdout.encoding and sys.stdout.encoding.lower() != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')


# ── HTTP 工具 ─────────────────────────────────────────────────────────────────

class ApiClient:
    def __init__(self, host: str, port: int):
        self.host = host
        self.port = port
        self._cookie = ""

    def _conn(self):
        return http.client.HTTPConnection(self.host, self.port, timeout=10)

    def _headers(self) -> dict:
        h = {"Content-Type": "application/json"}
        if self._cookie:
            h["Cookie"] = self._cookie
        return h

    def request(self, method: str, path: str, body: dict = None) -> tuple[int, dict]:
        try:
            conn = self._conn()
            data = json.dumps(body).encode() if body else b""
            conn.request(method, path, body=data, headers=self._headers())
            resp = conn.getresponse()
            status = resp.status
            raw = resp.read().decode(errors='replace')
            sc = resp.getheader("Set-Cookie", "")
            if "edgelink_sid=" in sc and "edgelink_sid=;" not in sc:
                sid = sc.split("edgelink_sid=")[1].split(";")[0]
                self._cookie = f"edgelink_sid={sid}"
            conn.close()
            try:
                return status, json.loads(raw)
            except Exception:
                return status, {"raw": raw}
        except ConnectionRefusedError:
            print(f"\n  ✗ 無法連線到 http://{self.host}:{self.port}")
            print(f"    請確認 EdgeLink 已啟動（Unity Editor Play 或執行 .exe）")
            sys.exit(1)
        except Exception as e:
            print(f"\n  ✗ HTTP 請求失敗: {e}")
            sys.exit(1)

    def login(self, password: str) -> bool:
        status, body = self.request("POST", "/api/auth/login", {"password": password})
        if status == 200 and body.get("success"):
            print("  [Auth] 登入成功")
            return True
        print(f"  [Auth] 登入失敗 HTTP {status}: {body}")
        return False

    def get(self, path: str) -> tuple[int, dict]:
        return self.request("GET", path)

    def post(self, path: str, body: dict = None) -> tuple[int, dict]:
        return self.request("POST", path, body)

    def put(self, path: str, body: dict) -> tuple[int, dict]:
        return self.request("PUT", path, body)


# ── 遮罩工具 ──────────────────────────────────────────────────────────────────

def ensure_mask(api: ApiClient, mask_id: str, definition: dict):
    status, _ = api.get(f"/api/masks/{mask_id}")
    if status == 404:
        s, b = api.post("/api/masks", {"maskId": mask_id, "localizationKey": mask_id})
        if s not in (200, 201):
            print(f"  [Mask] 建立 {mask_id} 失敗：{b}")
            return False
        print(f"  [Mask] 建立 {mask_id}")
    else:
        print(f"  [Mask] {mask_id} 已存在，更新定義")

    s, b = api.put(f"/api/masks/{mask_id}", {**definition, "maskId": mask_id})
    if s == 200 and b.get("success"):
        print(f"        ↳ 定義套用完成")
        return True
    print(f"  [Mask] {mask_id} PUT 失敗：{b}")
    return False


# ── Port 工具 ─────────────────────────────────────────────────────────────────

def get_all_ports(api: ApiClient) -> list[dict]:
    _, body = api.get("/api/ports")
    return body.get("ports", [])


def find_server_port(ports: list[dict], local_port: str) -> dict | None:
    return next((p for p in ports
                 if p.get("netProtocol") == "TCP Server"
                 and p.get("localPort") == local_port), None)


def find_client_port(ports: list[dict], remote_port: str) -> dict | None:
    return next((p for p in ports
                 if p.get("netProtocol") == "TCP Client"
                 and p.get("remotePort") == remote_port), None)


def ensure_server(api: ApiClient, local_port: str, name: str) -> str | None:
    ports = get_all_ports(api)
    existing = find_server_port(ports, local_port)
    if existing:
        print(f"  [Port] TCP Server :{local_port} ({existing['protocolName']}) 已存在  id={existing['id'][:8]}")
        _enable(api, existing)
        return existing["id"]

    s, b = api.post("/api/ports", {
        "protocolName": name,
        "netProtocol": "TCP Server",
        "localPort": local_port,
        "remotePort": "--",
        "maskType": "OriginalData",
        "responseMaskType": "",
        "requestMode": "serial",
    })
    if s not in (200, 201):
        print(f"  [Port] 建立 TCP Server :{local_port} 失敗：{b}")
        return None

    ports = get_all_ports(api)
    p = find_server_port(ports, local_port)
    if not p:
        print(f"  [Port] 找不到剛建立的 TCP Server :{local_port}")
        return None
    print(f"  [Port] TCP Server :{local_port} ({name}) 已建立  id={p['id'][:8]}")
    _enable(api, p)
    return p["id"]


def ensure_client(api: ApiClient, remote_port: str, name: str,
                   request_mode: str, mask_type: str,
                   response_mask: str, source_id: str):
    ports = get_all_ports(api)
    existing = find_client_port(ports, remote_port)
    if existing:
        print(f"  [Port] TCP Client :{remote_port} ({existing['protocolName']}) 已存在  id={existing['id'][:8]}")
        _enable(api, existing)
        return

    s, b = api.post("/api/ports", {
        "protocolName": name,
        "netProtocol": "TCP Client",
        "localPort": "--",
        "remotePort": remote_port,
        "targetIp": "127.0.0.1",
        "maskType": mask_type,
        "responseMaskType": response_mask,
        "requestMode": request_mode,
        "sourceProtocolId": source_id,
    })
    if s not in (200, 201):
        print(f"  [Port] 建立 TCP Client :{remote_port} 失敗：{b}")
        return

    ports = get_all_ports(api)
    p = find_client_port(ports, remote_port)
    if p:
        print(f"  [Port] TCP Client :{remote_port} ({name}, mode={request_mode}) 已建立  id={p['id'][:8]}")
        _enable(api, p)


def _enable(api: ApiClient, port: dict):
    if not port.get("isEnabled"):
        api.post(f"/api/ports/{port['id']}/enabled", {"enabled": True})
        print(f"        ↳ 已啟用")


# ── 主程式 ───────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="EdgeLink 測試環境完整設定")
    parser.add_argument("--host",     default="127.0.0.1")
    parser.add_argument("--web-port", type=int, default=8181, dest="web_port")
    parser.add_argument("--password", default="", dest="password",
                        help="Web UI 密碼（未設定可留空）")
    args = parser.parse_args()

    api = ApiClient(args.host, args.web_port)

    print(f"\nEdgeLink 測試環境完整設定")
    print(f"  目標: http://{args.host}:{args.web_port}")
    print()

    # ── 認證 ──────────────────────────────────────────────────────────────────
    if args.password:
        if not api.login(args.password):
            print("  ✗ 登入失敗，請確認密碼")
            sys.exit(1)
    else:
        status, _ = api.get("/api/ports")
        if status == 401:
            print("  ✗ 需要密碼，請加上 --password <密碼>")
            sys.exit(1)
        print("  [Auth] 無需密碼")

    # ── 遮罩 ──────────────────────────────────────────────────────────────────
    print("\n── 遮罩 ──────────────────────────────────────────────────────")

    ensure_mask(api, "ConcurrentMask", {
        "localizationKey":    "ConcurrentMask",
        "description":        "並發模式入站遮罩，注入 correlationId",
        "fieldDelimiter":     ";",
        "kvSeparator":        ":",
        "outputTemplate":     "ID:{ID};VALUE:{VALUE};_corrId:{_corrId}",
        "sampleData":         "",
        "routeMode":          "",
        "correlationIdField": "",
    })

    ensure_mask(api, "ResponseRaw", {
        "localizationKey":    "ResponseRaw",
        "description":        "單播回應遮罩，依 _corrId 路由回發送者",
        "fieldDelimiter":     ";",
        "kvSeparator":        ":",
        "outputTemplate":     "{raw}",
        "sampleData":         "",
        "routeMode":          "response",
        "correlationIdField": "_corrId",
    })

    ensure_mask(api, "BroadcastRaw", {
        "localizationKey":    "BroadcastRaw",
        "description":        "廣播回應遮罩，送給所有已連線 IoT client",
        "fieldDelimiter":     ";",
        "kvSeparator":        ":",
        "outputTemplate":     "{raw}",
        "sampleData":         "",
        "routeMode":          "broadcast",
        "correlationIdField": "",
    })

    # ── Ports ─────────────────────────────────────────────────────────────────
    print("\n── Ports ─────────────────────────────────────────────────────")

    # Concurrent pair: 9999 + 7777
    print("\n  [concurrent pair]")
    conc_id = ensure_server(api, "9999", "測試")
    if conc_id:
        ensure_client(api, "7777", "溫度設備",
                      request_mode="concurrent",
                      mask_type="ConcurrentMask",
                      response_mask="ResponseRaw",
                      source_id=conc_id)

    # Serial pair: 9998 + 7776
    print("\n  [serial pair]")
    serial_id = ensure_server(api, "9998", "test-serial-server")
    if serial_id:
        ensure_client(api, "7776", "test-serial-device",
                      request_mode="serial",
                      mask_type="OriginalData",
                      response_mask="ResponseRaw",
                      source_id=serial_id)

    # Polling pair: 9997 + 7775
    print("\n  [polling pair]")
    poll_id = ensure_server(api, "9997", "test-polling-server")
    if poll_id:
        ensure_client(api, "7775", "test-polling-device",
                      request_mode="polling",
                      mask_type="OriginalData",
                      response_mask="",
                      source_id=poll_id)

    # Broadcast pair: 9996 + 7774
    print("\n  [broadcast pair]")
    bcast_id = ensure_server(api, "9996", "test-broadcast-server")
    if bcast_id:
        ensure_client(api, "7774", "test-broadcast-device",
                      request_mode="serial",
                      mask_type="OriginalData",
                      response_mask="BroadcastRaw",
                      source_id=bcast_id)

    print()
    print("✓ 設定完成")
    print()
    print("現在可以執行：")
    print("  python tools/run_all_tests.py")


if __name__ == "__main__":
    main()
