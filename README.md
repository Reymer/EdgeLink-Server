<div align="center">

# EdgeLink Server

**IoT Protocol Bridge & Message Routing Server**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D4?logo=windows)](https://github.com/Reymer/EdgeLink-Server/releases)
[![License](https://img.shields.io/badge/License-GPL_3.0-blue)](LICENSE)
[![Version](https://img.shields.io/badge/Version-1.0.0-informational)](https://github.com/Reymer/EdgeLink-Server/releases/tag/v1.0.0)

A lightweight .NET 8 server that bridges IoT devices over TCP/UDP, transforms protocol data via custom Mask definitions, and provides a browser-based management interface. Runs as a Windows Service.

![Ports](https://github.com/Reymer/EdgeLink-Server/releases/download/v1.0.0/ports.png)

</div>

---

## Features

| Feature | Description |
|---------|-------------|
| **Multi-protocol** | TCP Server, TCP Client, UDP — each port configured independently |
| **Message Routing** | Automatically bridges two ports via `SourceProtocolId` |
| **Mask System** | Custom protocol parsing and transformation rules for IoT firmware output |
| **Real-time Monitor** | Per-port SSE-streamed log with keyword search and download |
| **Web UI** | Browser-based management interface — no frontend setup required |
| **HTTPS** | Auto-generated self-signed certificate with SAN for all local IPs |
| **Windows Service** | Auto-start on boot via `sc`, supports install / uninstall |
| **Security** | PBKDF2 password hashing, session persistence, Cookie SameSite, CORS allowlist |
| **File Logging** | Daily rolling log files with 7-day retention |

---

## Requirements

- Windows 10 / 11 x64
- Administrator privileges (for installation only)

---

## Installation

1. Download `EdgeLink-Server-v1.0.0-win-x64.zip` from [Releases](https://github.com/Reymer/EdgeLink-Server/releases)
2. Extract the zip
3. Run `install.bat` **as Administrator**
4. The service starts automatically
5. Open your browser and go to `http://localhost:8080`
   - Default password: `admin`

> **HTTPS:** Run `install.bat --https` to enable HTTPS. A self-signed certificate will be generated and trusted automatically on the local machine.

### Uninstall

Run `install.bat` as Administrator and choose uninstall, or run:

```bat
EdgeLinkServer.exe --uninstall
```

---

## Screenshots

### Login

![Login](https://github.com/Reymer/EdgeLink-Server/releases/download/v1.0.0/login.png)

### Monitor Log

![Monitor](https://github.com/Reymer/EdgeLink-Server/releases/download/v1.0.0/monitor.png)

---

## Web UI

| Tab | Description |
|-----|-------------|
| **Mask** | Create / edit / delete Mask definitions — field parsing rules and route mode |
| **Ports** | Add / remove / toggle ports, monitor connection status and traffic in real-time |
| **System Log** | View server logs with keyword filtering |

---

## API Reference

Base URL: `http://<host>:8080`

| Tag | Method | Path | Description |
|-----|--------|------|-------------|
| Auth | `POST` | `/api/auth/login` | Login |
| Auth | `POST` | `/api/auth/logout` | Logout |
| Auth | `GET` | `/api/auth/status` | Check auth status |
| Auth | `POST` | `/api/auth/change-password` | Change password |
| Ports | `GET` | `/api/ports` | List all ports |
| Ports | `POST` | `/api/ports` | Add port |
| Ports | `PUT` | `/api/ports/{id}` | Update port |
| Ports | `DELETE` | `/api/ports` | Delete port(s) |
| Ports | `POST` | `/api/ports/{id}/enabled` | Toggle port enabled |
| Ports | `GET` | `/api/ports/{id}/clients` | List TCP clients |
| Masks | `GET` | `/api/masks` | List all masks |
| Masks | `POST` | `/api/masks` | Add mask |
| Masks | `PUT` | `/api/masks/{id}` | Update mask |
| Masks | `DELETE` | `/api/masks/{id}` | Delete mask |
| Monitor | `GET` | `/api/monitor-stream` | SSE real-time stream |
| Monitor | `POST` | `/api/monitor/port` | Set monitor port |
| Logs | `GET` | `/api/logs` | System logs |
| Settings | `GET` | `/api/settings/export` | Export settings |
| Settings | `POST` | `/api/settings/import` | Import settings |

---

## Project Structure

```
EdgeLink-Server/
└── Server/
    ├── Infrastructure/      # AppConfig, AppLogger, AppPaths, CertificateHelper, ServiceManager
    ├── NetworkServer/
    │   ├── Base/            # Connector base, models (PortData, TCPServerData, ...)
    │   ├── TCP/             # TCPServerConnector, TCPClientConnector
    │   ├── Udp/             # UdpConnector
    │   ├── Router/          # NetworkMessageRouter
    │   ├── Services/        # PortManager, PortDataStorageService
    │   └── Logging/         # LogHelper, RouterLogHelper
    ├── WebApi/              # HttpApiServer, ApiRouter, Auth, Port, Mask, Monitor handlers
    ├── WebUI/               # Frontend HTML/CSS/JS
    ├── EdgeLinkService.cs   # BackgroundService (Generic Host)
    └── Program.cs           # Entry point
```

---

## CLI Options

```
EdgeLinkServer.exe [options]

  --port <n>          HTTP port (default: 8080)
  --https             Enable HTTPS
  --https-port <n>    HTTPS port (default: 8443)
  --cors <origins>    Comma-separated allowed CORS origins
  --install           Install as Windows Service (requires admin)
  --uninstall         Uninstall Windows Service (requires admin)

Environment variables:
  EDGELINK_PORT, EDGELINK_HTTPS, EDGELINK_HTTPS_PORT, EDGELINK_CORS
```

---

## Examples

| Example | Language | Description |
|---------|----------|-------------|
| [temperature-sensor](examples/temperature-sensor/) | English | Simulated sensor → EdgeLink → receiver, end-to-end demo |
| [temperature-sensor-zh](examples/temperature-sensor-zh/) | 繁體中文 | 溫度感測器完整範例（中文版） |

Each example includes a setup script, device simulator, and data receiver. Run `python setup.py` to configure EdgeLink automatically.

---

## Changelog

| Version | Changes |
|---------|---------|
| v1.0.0 | .NET 8 migration — removed Unity dependency; Windows Service, HTTPS, PBKDF2, session persistence, rolling file logger, CORS allowlist, SameSite cookies |

---

<div align="center">

**Extrakyo** · GPL-3.0

</div>
