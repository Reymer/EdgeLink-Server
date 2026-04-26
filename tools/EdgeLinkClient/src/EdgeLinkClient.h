#pragma once
#include <Arduino.h>
#include <Client.h>
#include <Udp.h>

typedef void (*EdgeLinkReceiveCallback)(const String& message);
// TCP 才有意義；UDP 模式下不會觸發
typedef void (*EdgeLinkStatusCallback)(bool connected);

class EdgeLinkClient {
public:
    EdgeLinkClient() = default;

    // ── TCP 模式 ────────────────────────────────────────
    // client      : WiFiClient / EthernetClient 實例（呼叫端持有）
    // host        : EdgeLink Server IP / hostname
    // port        : TCP Server 埠號
    // reconnectMs : 斷線後重試間隔（毫秒）
    void beginTCP(Client& client, const char* host, uint16_t port,
                  uint32_t reconnectMs = 5000);

    // ── UDP 模式 ────────────────────────────────────────
    // udp         : WiFiUDP / EthernetUDP 實例（呼叫端持有）
    // host        : EdgeLink Server IP / hostname
    // remotePort  : Server UDP 埠號
    // localPort   : 本地監聽埠（0 = 與 remotePort 相同）
    void beginUDP(UDP& udp, const char* host, uint16_t remotePort,
                  uint16_t localPort = 0);

    // ── 共用 API ────────────────────────────────────────
    void onReceive(EdgeLinkReceiveCallback cb);
    void onStatus(EdgeLinkStatusCallback cb); // UDP 模式下無效

    // 發送一行訊息（自動補 \n）
    // TCP：未連線時回傳 false
    // UDP：發送 UDP packet，WiFi/Ethernet 可用時為 true
    bool send(const String& message);

    // 必須在 loop() 中持續呼叫
    void update();

    // TCP：是否保持 TCP 連線
    // UDP：固定回傳 true（UDP 無連線概念）
    bool isConnected() const;

private:
    enum class Proto { NONE, TCP, UDP } proto = Proto::NONE;

    // TCP
    Client*     tcpClient    = nullptr;
    uint32_t    reconnectMs  = 5000;
    uint32_t    lastAttemptMs = 0;
    bool        wasConnected = false;

    // UDP
    UDP*        udpClient    = nullptr;
    uint16_t    remotePort   = 0;
    uint16_t    localPort    = 0;

    // 共用
    const char* host = nullptr;
    uint16_t    port = 0;

    String                   rxBuf;
    EdgeLinkReceiveCallback  cbReceive = nullptr;
    EdgeLinkStatusCallback   cbStatus  = nullptr;

    void updateTCP();
    void updateUDP();
    void handleLine(const String& line);
    bool sendRaw(const String& line); // 已含 \n
};
