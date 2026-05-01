<div align="center">

# EdgeLink Server

**IoT Protocol Bridge & Message Routing Server**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D4?logo=windows)](https://github.com/Reymer/EdgeLink-Server/releases)
[![License](https://img.shields.io/badge/License-GPL_3.0-blue)](LICENSE)
[![Version](https://img.shields.io/badge/Version-1.0.0-informational)](https://github.com/Reymer/EdgeLink-Server/releases/tag/v1.0.0)

A lightweight .NET 8 server that bridges IoT devices over TCP/UDP, transforms protocol data via custom Mask definitions, and provides a browser-based management interface.

</div>

---

## Features

| Feature | Description |
|---------|-------------|
| **Multi-protocol** | TCP Server, TCP Client, UDP — each port configured independently |
| **Message Routing** | Automatically bridges ports via `SourceProtocolId` |
| **Mask System** | Custom protocol parsing and transformation rules for IoT firmware output |
| **Real-time Monitor** | Per-port SSE-streamed log with keyword search |
| **Web UI** | Browser-based management — no frontend setup required |
| **HTTPS** | Auto-generated self-signed certificate with SAN for all local IPs |
| **Security** | PBKDF2 password hashing, session persistence, HttpOnly cookies, CORS allowlist |
| **File Logging** | Daily rolling log files with 7-day retention |
| **Unity SDK** | UPM package for receiving data in Unity (TCP / TCP Listener / UDP) |

---

## Requirements

- Windows 10 / 11 x64
- No .NET Runtime required (self-contained)

---

## Installation

1. Download `EdgeLink-Server-v1.0.0-win-x64.zip` from [Releases](https://github.com/Reymer/EdgeLink-Server/releases)
2. Extract the zip
3. Run `EdgeLinkServer.exe`
4. Open your browser at `https://localhost:8443`
   - Accept the self-signed certificate warning (click Advanced → Proceed)
   - Default password: `admin` — **change it immediately after login**

---

## Web UI

| Tab | Description |
|-----|-------------|
| **Ports** | Add / remove / toggle ports, monitor connection status and traffic in real-time |
| **Mask** | Create / edit / delete Mask definitions — field parsing rules and output templates |
| **System Log** | View server event logs with keyword filtering |

Full documentation available at `/manual` after starting the server.

---

## CLI Options

```
EdgeLinkServer.exe [options]

  --port <n>          HTTP port (default: 8080)
  --no-https          Disable HTTPS (HTTPS is enabled by default)
  --https-port <n>    HTTPS port (default: 8443)
  --cors <origins>    Comma-separated allowed CORS origins

Environment variables:
  EDGELINK_PORT, EDGELINK_HTTPS, EDGELINK_HTTPS_PORT, EDGELINK_CORS
```

Priority: CLI args > environment variables > defaults

---

## API Reference

Base URL: `https://<host>:8443`

All `/api/*` endpoints (except auth) require a session cookie. Interactive docs at `/docs`.

| Tag | Method | Path | Description |
|-----|--------|------|-------------|
| Auth | `POST` | `/api/auth/login` | Login — body: `{"password":"..."}` |
| Auth | `POST` | `/api/auth/logout` | Logout |
| Auth | `GET` | `/api/auth/status` | Check session status |
| Auth | `POST` | `/api/auth/change-password` | Change password |
| Ports | `GET` | `/api/ports` | List all ports |
| Ports | `POST` | `/api/ports` | Add port |
| Ports | `PUT` | `/api/ports/{id}` | Update port |
| Ports | `DELETE` | `/api/ports` | Delete port |
| Ports | `POST` | `/api/ports/{id}/enabled` | Toggle port enabled |
| Ports | `GET` | `/api/ports/{id}/clients` | List connected TCP clients |
| Masks | `GET` | `/api/masks` | List all mask IDs |
| Masks | `POST` | `/api/masks` | Add mask |
| Masks | `GET` | `/api/masks/{maskId}` | Get mask definition |
| Masks | `PUT` | `/api/masks/{maskId}` | Update mask |
| Masks | `DELETE` | `/api/masks/{maskId}` | Delete mask |
| Monitor | `GET` | `/api/monitor-stream` | SSE real-time message stream |
| Monitor | `POST` | `/api/monitor/port` | Set monitor port |
| Logs | `GET` | `/api/logs` | System logs (cursor pagination) |
| Settings | `GET` | `/api/settings/export` | Export all settings as JSON |
| Settings | `POST` | `/api/settings/import` | Import settings JSON |

---

## Unity SDK

The EdgeLink Unity SDK lets Unity applications receive data forwarded by EdgeLink Server. Supports TCP, TCP Listener, and UDP connection modes with automatic Mask fetching at runtime.

### Installation (UPM)

In Unity → **Window → Package Manager → + → Add package from git URL**:

```
https://github.com/Reymer/EdgeLink-Server.git?path=SDK/Unity/Package
```

Then import the **Basic Example** sample via Package Manager → EdgeLink SDK → Samples.

### Usage

Add `EdgeLinkManager` to any GameObject and configure in the Inspector:

| Field | Description |
|-------|-------------|
| Server URL | EdgeLink Server address for runtime Mask fetching |
| Password | Login password |
| Mask ID | Name of the Mask to apply for field parsing |
| Protocol | `TCP` / `TCPListener` / `UDP` |
| TCP Host / Port | (TCP mode) EdgeLink Server IP and port |
| Listen Port | (TCPListener mode) Local port Unity listens on |
| UDP Local Port | (UDP mode) Local UDP port |

```csharp
using UnityEngine;

public class Example : MonoBehaviour
{
    public GameObject edgeLinkObject;

    EdgeLinkManager edgeLink;
    string          lastRaw;

    void Start()
    {
        edgeLink = edgeLinkObject.GetComponent<EdgeLinkManager>();
    }

    void Update()
    {
        if (edgeLink.Raw == lastRaw) return;
        lastRaw = edgeLink.Raw;

        string temp   = edgeLink.Get("temp");
        string humid  = edgeLink.Get("humid");
        string status = edgeLink.Get("status");

        Debug.Log($"Temp:{temp} Humid:{humid} Status:{status}");
    }
}
```

| Member | Type | Description |
|--------|------|-------------|
| `Raw` | `string` | Latest raw message string (unparsed) |
| `Get(key)` | `string` | Latest parsed value by field name, `null` if not found |

---

## Arduino SDK

The EdgeLink Arduino Library lets ESP32 / ESP8266 / Arduino devices connect to EdgeLink Server. PING/PONG keepalive is handled automatically — no extra code needed.

### Installation

**Option A — ZIP import:**
1. Download `SDK/Arduino/EdgeLink.zip`
2. Arduino IDE → **Sketch → Include Library → Add .ZIP Library…**

**Option B — manual:**  
Copy the `SDK/Arduino/EdgeLink/` folder into your Arduino `libraries/` directory.

### TCP Example (ESP32 / ESP8266)

```cpp
#include <WiFi.h>
#include <EdgeLink.h>

WiFiClient  wifiClient;
EdgeLinkTCP edgelink(wifiClient);

void onMessage(const String& msg) {
    Serial.println(msg);  // response from backend
}

void setup() {
    WiFi.begin("your-ssid", "your-password");
    while (WiFi.status() != WL_CONNECTED) delay(500);

    edgelink.onMessage(onMessage);
    edgelink.setAutoReconnect(true, 5000);
    edgelink.begin("192.168.1.100", 9001);  // EdgeLink TCP Server port
}

void loop() {
    edgelink.loop();  // must be called — handles PING/PONG and receive

    static uint32_t t = 0;
    if (millis() - t >= 3000 && edgelink.isConnected()) {
        edgelink.send("id:ESP32_01;temp:25.3;humidity:60.0");
        t = millis();
    }
}
```

### UDP Example (ESP32 / ESP8266)

```cpp
#include <WiFi.h>
#include <WiFiUdp.h>
#include <EdgeLink.h>

WiFiUDP     wifiUdp;
EdgeLinkUDP edgelink(wifiUdp);

void onMessage(const String& msg, IPAddress ip, uint16_t port) {
    Serial.println(msg);
}

void setup() {
    WiFi.begin("your-ssid", "your-password");
    while (WiFi.status() != WL_CONNECTED) delay(500);

    edgelink.begin(4210);           // local receive port
    edgelink.onMessage(onMessage);
}

void loop() {
    edgelink.loop();

    static uint32_t t = 0;
    if (millis() - t >= 3000) {
        edgelink.send("192.168.1.100", 9002, "id:ESP32_01;temp:25.3;humidity:60.0");
        t = millis();
    }
}
```

### API

| Class | Method | Description |
|-------|--------|-------------|
| `EdgeLinkTCP` | `begin(host, port)` | Connect to EdgeLink TCP Server port |
| | `loop()` | Must call in `loop()` — handles PING/PONG and receive |
| | `send(msg)` | Send a message (newline appended automatically) |
| | `onMessage(cb)` | Callback for incoming messages (EDGELINK_* filtered out) |
| | `isConnected()` | Returns connection state |
| | `setAutoReconnect(enable, ms)` | Auto-reconnect on disconnect (default: enabled, 5000 ms) |
| `EdgeLinkUDP` | `begin(localPort = 0)` | Start listening on local UDP port (`0` = send-only) |
| | `loop()` | Must call in `loop()` — receives incoming packets |
| | `send(host, port, msg)` | Send UDP packet to EdgeLink |
| | `onMessage(cb)` | Callback with `(msg, remoteIP, remotePort)` |

---

## Project Structure

```
EdgeLink-Server/
├── Server/
│   ├── Infrastructure/      # AppConfig, AppLogger, AppPaths, CertificateHelper
│   ├── NetworkServer/
│   │   ├── Base/            # Connector base, models (PortData, ...)
│   │   ├── TCP/             # TCPServerConnector, TCPClientConnector
│   │   ├── Udp/             # UdpConnector
│   │   ├── Router/          # NetworkMessageRouter
│   │   └── Services/        # PortManager, PortDataStorageService
│   ├── WebApi/              # HttpApiServer, Auth/Port/Mask/Monitor handlers
│   ├── WebUI/               # Frontend HTML/CSS/JS (index, manual, docs)
│   └── Program.cs           # Entry point
└── SDK/
    ├── Unity/
    │   └── Package/         # UPM package (Runtime + Editor + Samples~)
    └── Arduino/
        └── EdgeLink/        # Arduino library (TCP + UDP, PING/PONG auto-handling)
```

---

## Changelog

| Version | Changes |
|---------|---------|
| v1.0.0 | Initial release — .NET 8, HTTPS by default, PBKDF2, session persistence, rolling logger, CORS, Unity SDK |

---

<div align="center">

**Extrakyo** · GPL-3.0

</div>
