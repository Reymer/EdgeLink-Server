/**
 * EdgeLink Client — TCP 模式範例
 * 適用：ESP32 / ESP8266（WiFi）
 *
 * 特性：
 *   - 持久連線，斷線自動重連
 *   - 自動回應 RTT Ping（EDGELINK_PING / EDGELINK_PONG）
 *   - onStatus callback 通知連線狀態
 */

#include <WiFi.h>           // ESP32
// #include <ESP8266WiFi.h> // ESP8266

#include <EdgeLinkClient.h>

// ── 設定區 ────────────────────────────────────────────
const char* WIFI_SSID  = "your_ssid";
const char* WIFI_PASS  = "your_password";
const char* SERVER_IP  = "192.168.1.100";
const uint16_t PORT    = 8888;
// ──────────────────────────────────────────────────────

WiFiClient wifiClient;
EdgeLinkClient edgelink;

void onMessage(const String& msg) {
    Serial.print("[RX] ");
    Serial.println(msg);
}

void onStatus(bool connected) {
    Serial.println(connected ? "[EdgeLink] Connected" : "[EdgeLink] Disconnected, retrying...");
}

void setup() {
    Serial.begin(115200);

    WiFi.begin(WIFI_SSID, WIFI_PASS);
    Serial.print("Connecting WiFi");
    while (WiFi.status() != WL_CONNECTED) { delay(500); Serial.print('.'); }
    Serial.println("\nIP: " + WiFi.localIP().toString());

    edgelink.beginTCP(wifiClient, SERVER_IP, PORT);
    edgelink.onReceive(onMessage);
    edgelink.onStatus(onStatus);
}

void loop() {
    edgelink.update(); // 必須持續呼叫

    static uint32_t lastSend = 0;
    if (millis() - lastSend >= 2000) {
        lastSend = millis();
        float temp = random(200, 350) / 10.0f;
        bool ok = edgelink.send("Temp=" + String(temp, 1));
        Serial.println(ok ? "[TX] sent" : "[TX] not connected");
    }
}
