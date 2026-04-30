# Example: Temperature Sensor

Demonstrates a complete end-to-end flow:

```
device.py  →  EdgeLink :9001  →  [SensorMask]  →  EdgeLink :9002  →  receiver.py
```

A simulated IoT sensor sends raw `TEMP:24.5;HUM:62.3` data to EdgeLink.
EdgeLink parses the fields using the **SensorMask** and forwards the
formatted result to the receiver.

---

## Requirements

- Python 3.8+
- EdgeLink Server running on `localhost:8080`

---

## Quick Start

### Step 1 — Configure EdgeLink

Run the setup script once to create the required Mask and Ports:

```bash
python setup.py
```

Optional arguments:

```bash
python setup.py --host 192.168.0.10 --port 8080 --password yourpassword
```

This creates:
- **SensorMask** — parses `TEMP`/`HUM` fields, formats output
- **SensorInput** — TCP Server on port `9001` (device connects here)
- **SensorOutput** — TCP Client on port `9002` (forwards to receiver)

### Step 2 — Start the receiver

In terminal 1:

```bash
python receiver.py
```

### Step 3 — Start the device simulator

In terminal 2:

```bash
python device.py
```

---

## Expected Output

**device.py**
```
Temperature Sensor — connecting to 127.0.0.1:9001...
✓ Connected to EdgeLink at 127.0.0.1:9001
  Sending readings every 1.0s  (Ctrl+C to stop)

  → Sent: TEMP:24.5;HUM:62.3
  → Sent: TEMP:25.1;HUM:60.8
  → Sent: TEMP:23.9;HUM:63.1
```

**receiver.py**
```
Data Receiver — listening on 127.0.0.1:9002
  Waiting for EdgeLink to connect...

✓ EdgeLink connected from 127.0.0.1:xxxxx

  [14:32:01] Temperature: 24.5C | Humidity: 62.3%
  [14:32:02] Temperature: 25.1C | Humidity: 60.8%
  [14:32:03] Temperature: 23.9C | Humidity: 63.1%
```

---

## How It Works

### SensorMask Definition

| Field | Value |
|-------|-------|
| Field Delimiter | `;` |
| KV Separator | `:` |
| Output Template | `Temperature: {TEMP}C \| Humidity: {HUM}%` |
| Route Mode | `broadcast` |

EdgeLink splits the incoming message by `;`, then by `:` to extract
key-value pairs (`TEMP`, `HUM`), and renders the output template.

### Port Configuration

| Port | Type | Direction |
|------|------|-----------|
| 9001 | TCP Server | Device → EdgeLink |
| 9002 | TCP Client | EdgeLink → Receiver |

The two ports are linked via `SourceProtocolId` so messages received
on port 9001 are automatically routed to port 9002 after masking.

---

## Adapting to Your Device

To use a real device instead of `device.py`:

1. Connect your device to `<EdgeLink-host>:9001` via TCP
2. Send data in `KEY:value;KEY:value` format (or adjust the Mask)
3. `receiver.py` (or your application) connects to `<EdgeLink-host>:9002`

To change the data format, edit the Mask definition in the Web UI
at `http://localhost:8080` → **Mask** tab → **SensorMask**.
