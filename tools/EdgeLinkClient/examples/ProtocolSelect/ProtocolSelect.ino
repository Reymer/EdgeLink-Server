/**
 * EdgeLink Client — TCP / UDP 切換範例
 * 適用：ESP32 / ESP8266
 *
 * 只需改第一行的 #define 決定要用哪個協議：
 *   #define USE_TCP   → TCP（持久連線、自動重連、支援 RTT 量測）
 *   #define USE_UDP   → UDP（無連線、高頻低延遲）
 */

#define USE_TCP
// #define USE_UDP

// ── 設定區 ────────────────────────────────────────────
const char* WIFI_SSID  = "your_ssid";
const char* WIFI_PASS  = "your_password";
const char* SERVER_IP  = "192.168.1.100";

#if defined(USE_TCP)
  const uint16_t SERVER_PORT = 8888; // EdgeLink TCP Server 埠號
#elif defined(USE_UDP)
  const uint16_t SERVER_PORT = 9000; // EdgeLink UDP 埠號
  const uint16_t LOCAL_PORT  = 9000; // 本地監聽埠
#endif
// ──────────────────────────────────────────────────────

#include <WiFi.h>
// #include <ESP8266WiFi.h>  // ESP8266 換這行

#include <EdgeLinkClient.h>

#if defined(USE_TCP)
  WiFiClient transport;
#elif defined(USE_UDP)
  #include <WiFiUdp.h>
  WiFiUDP transport;
#endif

EdgeLinkClient edgelink;

// ── Callbacks ─────────────────────────────────────────

void onMessage(const String& msg) {
    Serial.print("[RX] ");
    Serial.println(msg);
}

#if defined(USE_TCP)
void onStatus(bool connected) {
    Serial.println(connected
        ? "[EdgeLink] Connected to server"
        : "[EdgeLink] Disconnected, retrying...");
}
#endif

// ── Setup ─────────────────────────────────────────────

void setup() {
    Serial.begin(115200);

    // 連接 WiFi
    WiFi.begin(WIFI_SSID, WIFI_PASS);
    Serial.print("Connecting WiFi");
    while (WiFi.status() != WL_CONNECTED) {
        delay(500);
        Serial.print('.');
    }
    Serial.println();
    Serial.print("WiFi IP: ");
    Serial.println(WiFi.localIP());

#if defined(USE_TCP)
    Serial.println("Protocol: TCP");
    edgelink.beginTCP(transport, SERVER_IP, SERVER_PORT);
    edgelink.onReceive(onMessage);
    edgelink.onStatus(onStatus);

#elif defined(USE_UDP)
    Serial.println("Protocol: UDP");
    edgelink.beginUDP(transport, SERVER_IP, SERVER_PORT, LOCAL_PORT);
    edgelink.onReceive(onMessage);
#endif
}

// ── Loop ──────────────────────────────────────────────

void loop() {
    edgelink.update(); // 必須持續呼叫

    static uint32_t lastSend = 0;
    if (millis() - lastSend >= 2000) {
        lastSend = millis();

        // 模擬感測器資料
        float temp = random(200, 350) / 10.0f;
        float humi = random(400, 900) / 10.0f;
        String payload = "Temp=" + String(temp, 1) + ",Humi=" + String(humi, 1);

        bool ok = edgelink.send(payload);
        Serial.print("[TX] " + payload);
        Serial.println(ok ? " (ok)" : " (failed)");
    }
}
