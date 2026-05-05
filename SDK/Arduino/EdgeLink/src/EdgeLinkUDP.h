#pragma once
#include <Arduino.h>
#include <Udp.h>

class EdgeLinkUDP {
public:
    using MessageCallback = void (*)(const String& message, IPAddress remoteIP, uint16_t remotePort);

    explicit EdgeLinkUDP(UDP& udp);

    // Start listening on localPort (0 = send-only)
    bool begin(uint16_t localPort = 0);

    // Must be called in loop() — checks for incoming packets
    void loop();

    // Send a message to EdgeLink UDP port
    bool send(const char* host, uint16_t port, const String& message);
    bool send(IPAddress ip,     uint16_t port, const String& message);

    // Callback for incoming messages
    void onMessage(MessageCallback cb);

private:
    UDP&            _udp;
    MessageCallback _onMsg = nullptr;
};
