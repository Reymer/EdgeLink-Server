using EdgeLink;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace EdgeLinkSdk.Tests
{
    /// <summary>
    /// 整合測試：用真實 socket 驗證 EdgeLinkReceiver 端到端行為。
    /// IoT Server 的輸出格式：每則訊息後附加 \n。
    /// </summary>
    public class EdgeLinkReceiverTcpTests
    {
        // 每個測試用不同 port，避免 TIME_WAIT 衝突
        private const int Base = 17001;

        private static MaskDefinition TextMask() => new MaskDefinition
        {
            maskId = "test",
            inputEncoding = "text",
            fieldDelimiter = ";",
            kvSeparator = ":",
            outputTemplate = "ID={ID};TEMP={TEMP}",
        };

        // ── 基本接收 ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Tcp_SingleMessage_Received()
        {
            using var receiver = new EdgeLinkReceiver(Protocol.TCP);
            receiver.Start(Base);

            await SendLine(Base, "hello\n");
            await Task.Delay(100);
            receiver.Flush();

            Assert.NotNull(receiver.LatestMessage);
            Assert.Equal("hello", receiver.LatestMessage.Raw);
        }

        [Fact]
        public async Task Tcp_MultipleMessages_AllReceived()
        {
            using var receiver = new EdgeLinkReceiver(Protocol.TCP);
            int count = 0;
            receiver.OnMessage += _ => count++;
            receiver.Start(Base + 1);

            await SendLine(Base + 1, "msg1\nmsg2\nmsg3\n");
            await Task.Delay(150);
            receiver.Flush();

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task Tcp_ConnectionChanged_FiredOnConnectAndDisconnect()
        {
            using var receiver = new EdgeLinkReceiver(Protocol.TCP);
            int connects = 0, disconnects = 0;
            receiver.OnConnectionChanged += c => { if (c) connects++; else disconnects++; };
            receiver.Start(Base + 2);

            using (var client = new TcpClient())
            {
                await client.ConnectAsync("127.0.0.1", Base + 2);
                await Task.Delay(80);
                receiver.Flush();
            } // client 關閉 → server 偵測斷線

            await Task.Delay(150);
            receiver.Flush();

            Assert.Equal(1, connects);
            Assert.Equal(1, disconnects);
        }

        // ── 遮罩整合 ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Tcp_ParseOutput_ExtractsFields()
        {
            var mask = TextMask();
            using var receiver = new EdgeLinkReceiver(Protocol.TCP, mask);
            var parser = new MaskParser(mask);
            IotMessage received = null;
            receiver.OnMessage += m => received = m;
            receiver.Start(Base + 3);

            // 模擬 IoT Server 套用遮罩後的輸出
            await SendLine(Base + 3, "ID=7;TEMP=25\n");
            await Task.Delay(100);
            receiver.Flush();

            Assert.NotNull(received);
            var fields = parser.ParseOutput(received.Raw);
            Assert.Equal("7",  fields["ID"]);
            Assert.Equal("25", fields["TEMP"]);
        }

        // ── 停止/釋放 ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Tcp_StopAndDispose_NoException()
        {
            var receiver = new EdgeLinkReceiver(Protocol.TCP);
            receiver.Start(Base + 4);
            await Task.Delay(30);
            receiver.Stop();
            receiver.Dispose(); // 重複呼叫不應拋例外
        }

        // ── 工具 ──────────────────────────────────────────────────────────────

        private static async Task SendLine(int port, string message)
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var bytes = Encoding.UTF8.GetBytes(message);
            await client.GetStream().WriteAsync(bytes, 0, bytes.Length);
            await Task.Delay(30);
        }
    }

    public class EdgeLinkReceiverUdpTests
    {
        private const int Base = 17100;

        // ── 基本接收 ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Udp_SingleMessage_Received()
        {
            using var receiver = new EdgeLinkReceiver(Protocol.UDP);
            receiver.Start(Base);

            await SendPacket(Base, "hello");
            await Task.Delay(100);
            receiver.Flush();

            Assert.NotNull(receiver.LatestMessage);
            Assert.Equal("hello", receiver.LatestMessage.Raw);
        }

        [Fact]
        public async Task Udp_MultiplePackets_AllReceived()
        {
            using var receiver = new EdgeLinkReceiver(Protocol.UDP);
            int count = 0;
            receiver.OnMessage += _ => count++;
            receiver.Start(Base + 1);

            await SendPacket(Base + 1, "msg1");
            await SendPacket(Base + 1, "msg2");
            await SendPacket(Base + 1, "msg3");
            await Task.Delay(150);
            receiver.Flush();

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task Udp_TrailingNewline_IsStripped()
        {
            using var receiver = new EdgeLinkReceiver(Protocol.UDP);
            receiver.Start(Base + 2);

            // IoT Server 會附加 \n 後送出
            await SendPacket(Base + 2, "data\n");
            await Task.Delay(100);
            receiver.Flush();

            Assert.Equal("data", receiver.LatestMessage?.Raw);
        }

        [Fact]
        public async Task Udp_EmptyPacket_Ignored()
        {
            using var receiver = new EdgeLinkReceiver(Protocol.UDP);
            receiver.Start(Base + 3);

            await SendPacket(Base + 3, "   ");
            await Task.Delay(100);
            receiver.Flush();

            Assert.Null(receiver.LatestMessage);
        }

        [Fact]
        public async Task Udp_StopAndDispose_NoException()
        {
            var receiver = new EdgeLinkReceiver(Protocol.UDP);
            receiver.Start(Base + 4);
            await Task.Delay(30);
            receiver.Stop();
            receiver.Dispose();
        }

        // ── 工具 ──────────────────────────────────────────────────────────────

        private static async Task SendPacket(int port, string message)
        {
            using var client = new UdpClient();
            var bytes = Encoding.UTF8.GetBytes(message);
            await client.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, port));
        }
    }
}
