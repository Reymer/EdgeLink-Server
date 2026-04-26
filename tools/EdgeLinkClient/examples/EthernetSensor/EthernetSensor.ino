/**
 * EdgeLink Client — 有線 Ethernet TCP 範例
 * 適用：Arduino Uno/Mega + W5100/W5500 Shield
 */

#include <SPI.h>
#include <Ethernet.h>
#include <EdgeLinkClient.h>

// ── 設定區 ────────────────────────────────────────────
byte MAC[]             = { 0xDE, 0xAD, 0xBE, 0xEF, 0xFE, 0x01 };
const char* SERVER_IP  = "192.168.1.100";
const uint16_t PORT    = 8888;
// ──────────────────────────────────────────────────────

EthernetClient ethClient;
EdgeLinkClient edgelink;

void onMessage(const String& msg) {
    Serial.print("[RX] ");
    Serial.println(msg);
}

void onStatus(bool connected) {
    Serial.println(connected ? "[EdgeLink] Connected" : "[EdgeLink] Disconnected");
}

void setup() {
    Serial.begin(115200);
    Ethernet.begin(MAC);
    delay(1000);
    Serial.print("Ethernet IP: ");
    Serial.println(Ethernet.localIP());

    edgelink.beginTCP(ethClient, SERVER_IP, PORT);
    edgelink.onReceive(onMessage);
    edgelink.onStatus(onStatus);
}

void loop() {
    edgelink.update();

    static uint32_t lastSend = 0;
    if (millis() - lastSend >= 2000) {
        lastSend = millis();
        edgelink.send("Temp=" + String(analogRead(A0) / 10.0f, 1));
    }
}
