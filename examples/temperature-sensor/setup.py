"""
EdgeLink Example — Temperature Sensor Setup
============================================
Automatically configures EdgeLink via API:
  - Creates a Mask that parses TEMP/HUM fields
  - Creates a TCP Server port (device connects here)
  - Creates a TCP Client port (forwards to receiver)

Usage:
  python setup.py
  python setup.py --host 192.168.0.10 --port 8080 --password yourpassword
"""

import http.client
import json
import argparse
import sys


# ── API Client ────────────────────────────────────────────────────────────────

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
            print(f"\n✗ Cannot connect to http://{self.host}:{self.port}")
            print(f"  Make sure EdgeLink Server is running.")
            sys.exit(1)
        finally:
            conn.close()

    def login(self, password: str):
        status, body = self._request("POST", "/api/auth/login", {"password": password})
        if status == 200 and body.get("success"):
            print("✓ Logged in")
        else:
            print(f"✗ Login failed: {body.get('error', 'unknown')}")
            sys.exit(1)

    def get(self, path):    return self._request("GET",    path)
    def post(self, path, body=None): return self._request("POST", path, body)
    def put(self, path, body):       return self._request("PUT",  path, body)


# ── Helpers ───────────────────────────────────────────────────────────────────

def ensure_mask(api: EdgeLinkClient, mask_id: str, definition: dict):
    status, _ = api.get(f"/api/masks/{mask_id}")
    if status == 404:
        s, b = api.post("/api/masks", {"maskId": mask_id, "localizationKey": mask_id})
        if s not in (200, 201):
            print(f"  ✗ Failed to create mask '{mask_id}': {b}")
            return
        print(f"  + Created mask '{mask_id}'")
    else:
        print(f"  ~ Mask '{mask_id}' already exists, updating...")

    api.put(f"/api/masks/{mask_id}", {**definition, "maskId": mask_id})
    print(f"    Definition applied.")


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
        print(f"  ~ TCP Server :{port_number} already exists (id={existing['id'][:8]})")
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
        print(f"  + Created TCP Server :{port_number} (id={p['id'][:8]})")
        return p["id"]
    return None


def create_client_port(api: EdgeLinkClient, port_number: int, name: str,
                        mask: str, response_mask: str, source_id: str):
    ports = get_ports(api)
    existing = find_port(ports, "TCP Client", port_number)
    if existing:
        print(f"  ~ TCP Client :{port_number} already exists")
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
    print(f"  + Created TCP Client :{port_number}")


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="EdgeLink temperature sensor example setup")
    parser.add_argument("--host",     default="127.0.0.1", help="EdgeLink host (default: 127.0.0.1)")
    parser.add_argument("--port",     type=int, default=8080, help="EdgeLink HTTP port (default: 8080)")
    parser.add_argument("--password", default="admin", help="Web UI password (default: admin)")
    args = parser.parse_args()

    api = EdgeLinkClient(args.host, args.port)

    print(f"\nEdgeLink Temperature Sensor Example — Setup")
    print(f"Target: http://{args.host}:{args.port}\n")

    api.login(args.password)

    # ── Mask ──────────────────────────────────────────────────────────────────
    print("\n[Mask]")
    ensure_mask(api, "SensorMask", {
        "localizationKey": "SensorMask",
        "description":     "Parses TEMP/HUM fields and formats output",
        "fieldDelimiter":  ";",
        "kvSeparator":     ":",
        "outputTemplate":  "Temperature: {TEMP}C | Humidity: {HUM}%",
        "routeMode":       "broadcast",
    })

    # ── Ports ─────────────────────────────────────────────────────────────────
    print("\n[Ports]")

    # TCP Server :9001 — device connects here
    server_id = create_server_port(api, 9001, "SensorInput")

    if server_id:
        # TCP Client :9002 — forwards processed data to receiver.py
        create_client_port(api, 9002, "SensorOutput",
                            mask="SensorMask",
                            response_mask="OriginalData",
                            source_id=server_id)

    print("\n✓ Setup complete!\n")
    print("Now run in separate terminals:")
    print("  1.  python receiver.py")
    print("  2.  python device.py")
    print()
    print("Data flow:")
    print("  device.py  →  EdgeLink :9001  →  [SensorMask]  →  EdgeLink :9002  →  receiver.py")


if __name__ == "__main__":
    main()
