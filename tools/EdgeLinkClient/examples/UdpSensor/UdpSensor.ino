/**
 * EdgeLink Client — UDP 模式範例
 * 適用：ESP32 / ESP8266（WiFi）
 *
 * 特性：
 *   - 無連線概念，隨時可送出
 *   - 無 RTT 量測（UDP 模式不支援）
 *   - 適合高頻、低延遲、可接受少量封包遺失的場景
 */

#include <WiFi.h>
#include <WiFiUdp.h>
#include <EdgeLinkClient.h>

// ── 設定區 ────────────────────────────────────────────
const char* WIFI_SSID    = "your_ssid";
const char* WIFI_PASS    = "your_password";
const char* SERVER_IP    = "192.168.1.100";
const uint16_t REMOTE_PORT = 9000; // EdgeLink UDP 埠號
const uint16_t LOCAL_PORT  = 9000; // 本地監聽埠（可與 REMOTE_PORT 相同）
// ──────────────────────────────────────────────────────

WiFiUDP wifiUdp;
EdgeLinkClient edgelink;

void onMessage(const String& msg) {
    Serial.print("[RX] ");
    Serial.println(msg);
}

void setup() {
    Serial.begin(115200);

    WiFi.begin(WIFI_SSID, WIFI_PASS);
    Serial.print("Connecting WiFi");
    while (WiFi.status() != WL_CONNECTED) { delay(500); Serial.print('.'); }
    Serial.println("\nIP: " + WiFi.localIP().toString());

    edgelink.beginUDP(wifiUdp, SERVER_IP, REMOTE_PORT, LOCAL_PORT);
    edgelink.onReceive(onMessage); // 有 server → device 的封包時觸發
}

void loop() {
    edgelink.update(); // 必須持續呼叫（接收用）

    static uint32_t lastSend = 0;
    if (millis() - lastSend >= 1000) {
        lastSend = millis();
        float temp = random(200, 350) / 10.0f;
        edgelink.send("Temp=" + String(temp, 1));
    }
}
