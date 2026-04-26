#include "EdgeLinkClient.h"

static const char PING_PREFIX[] = "EDGELINK_PING:";
static const char PONG_PREFIX[] = "EDGELINK_PONG:";
static const uint16_t RX_BUF_MAX = 1024;

// ── begin ──────────────────────────────────────────────────────────────────

void EdgeLinkClient::beginTCP(Client& client, const char* h, uint16_t p,
                               uint32_t reconnMs) {
    proto        = Proto::TCP;
    tcpClient    = &client;
    host         = h;
    port         = p;
    reconnectMs  = reconnMs;
    lastAttemptMs = 0;
    wasConnected  = false;
    rxBuf         = "";
}

void EdgeLinkClient::beginUDP(UDP& udp, const char* h, uint16_t remPort,
                               uint16_t locPort) {
    proto      = Proto::UDP;
    udpClient  = &udp;
    host       = h;
    remotePort = remPort;
    localPort  = locPort ? locPort : remPort;
    port       = remPort;
    rxBuf      = "";
    udpClient->begin(localPort);
}

// ── callbacks ──────────────────────────────────────────────────────────────

void EdgeLinkClient::onReceive(EdgeLinkReceiveCallback cb) { cbReceive = cb; }
void EdgeLinkClient::onStatus(EdgeLinkStatusCallback cb)   { cbStatus  = cb; }

// ── isConnected ────────────────────────────────────────────────────────────

bool EdgeLinkClient::isConnected() const {
    if (proto == Proto::TCP) return tcpClient && tcpClient->connected();
    return true; // UDP 無連線概念
}

// ── send ───────────────────────────────────────────────────────────────────

bool EdgeLinkClient::send(const String& message) {
    String line = message;
    if (!line.endsWith("\n")) line += '\n';
    return sendRaw(line);
}

bool EdgeLinkClient::sendRaw(const String& line) {
    if (proto == Proto::TCP) {
        if (!tcpClient || !tcpClient->connected()) return false;
        return tcpClient->print(line) > 0;
    }
    if (proto == Proto::UDP) {
        if (!udpClient) return false;
        udpClient->beginPacket(host, remotePort);
        udpClient->print(line);
        return udpClient->endPacket() == 1;
    }
    return false;
}

// ── update ─────────────────────────────────────────────────────────────────

void EdgeLinkClient::update() {
    if      (proto == Proto::TCP) updateTCP();
    else if (proto == Proto::UDP) updateUDP();
}

void EdgeLinkClient::updateTCP() {
    bool connected = tcpClient && tcpClient->connected();

    if (connected != wasConnected) {
        wasConnected = connected;
        if (cbStatus) cbStatus(connected);
    }

    if (!connected) {
        rxBuf = "";
        uint32_t now = millis();
        if (now - lastAttemptMs >= reconnectMs) {
            lastAttemptMs = now;
            tcpClient->connect(host, port);
        }
        return;
    }

    while (tcpClient->available()) {
        char c = (char)tcpClient->read();
        if (c == '\n') {
            rxBuf.trim();
            if (rxBuf.length() > 0) handleLine(rxBuf);
            rxBuf = "";
        } else if (c != '\r') {
            rxBuf += c;
            if (rxBuf.length() >= RX_BUF_MAX) rxBuf = "";
        }
    }
}

void EdgeLinkClient::updateUDP() {
    if (!udpClient) return;
    int packetSize = udpClient->parsePacket();
    if (packetSize <= 0) return;

    String packet = "";
    while (udpClient->available()) {
        char c = (char)udpClient->read();
        if (c != '\r') packet += c;
    }

    // UDP 可能一個 packet 含多行（少見但處理）
    int start = 0;
    while (start < (int)packet.length()) {
        int nl = packet.indexOf('\n', start);
        String line = (nl >= 0) ? packet.substring(start, nl) : packet.substring(start);
        line.trim();
        if (line.length() > 0) handleLine(line);
        if (nl < 0) break;
        start = nl + 1;
    }
}

// ── handleLine ─────────────────────────────────────────────────────────────

void EdgeLinkClient::handleLine(const String& line) {
    // TCP 模式下回應 RTT Ping
    if (proto == Proto::TCP && line.startsWith(PING_PREFIX)) {
        sendRaw(String(PONG_PREFIX) + line.substring(strlen(PING_PREFIX)) + '\n');
        return;
    }

    if (cbReceive) cbReceive(line);
}
