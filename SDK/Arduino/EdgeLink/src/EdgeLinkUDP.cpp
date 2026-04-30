#include "EdgeLinkUDP.h"

EdgeLinkUDP::EdgeLinkUDP(UDP& udp) : _udp(udp) {}

bool EdgeLinkUDP::begin(uint16_t localPort) {
    if (localPort == 0) return true;
    return _udp.begin(localPort) == 1;
}

void EdgeLinkUDP::loop() {
    int size = _udp.parsePacket();
    if (size <= 0) return;

    String msg;
    msg.reserve(size);
    while (_udp.available()) {
        msg += (char)_udp.read();
    }
    msg.trim();

    if (msg.length() > 0 && _onMsg) {
        _onMsg(msg, _udp.remoteIP(), _udp.remotePort());
    }
}

bool EdgeLinkUDP::send(const char* host, uint16_t port, const String& message) {
    if (!_udp.beginPacket(host, port)) return false;
    _udp.print(message);
    return _udp.endPacket() == 1;
}

bool EdgeLinkUDP::send(IPAddress ip, uint16_t port, const String& message) {
    if (!_udp.beginPacket(ip, port)) return false;
    _udp.print(message);
    return _udp.endPacket() == 1;
}

void EdgeLinkUDP::onMessage(MessageCallback cb) {
    _onMsg = cb;
}
