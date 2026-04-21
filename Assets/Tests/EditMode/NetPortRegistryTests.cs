using NUnit.Framework;
using System.Linq;
using System.Threading.Tasks;

public class NetPortRegistryTests
{
    private NetPortRegistry registry;

    [SetUp]
    public void SetUp() => registry = new NetPortRegistry();

    private static PortData MakePort(string name) => new PortData
    {
        ProtocolName = name,
        LocalPortDetails  = new PortDetails { Port = "8080" },
        RemotePortDetails = new PortDetails { Port = "9090" },
    };

    // ── CRUD ─────────────────────────────────────────────────────────────────

    [Test]
    public void Add_And_Get_ReturnsSameInstance()
    {
        var port = MakePort("TCP1");
        registry.Add(NetProtocolType.TcpServer, "TCP1", port);

        Assert.AreSame(port, registry.Get(NetProtocolType.TcpServer, "TCP1"));
    }

    [Test]
    public void Get_MissingKey_ReturnsNull()
    {
        Assert.IsNull(registry.Get(NetProtocolType.TcpServer, "missing"));
    }

    [Test]
    public void Contains_ExistingKey_ReturnsTrue()
    {
        registry.Add(NetProtocolType.TcpClient, "C1", MakePort("C1"));
        Assert.IsTrue(registry.Contains(NetProtocolType.TcpClient, "C1"));
    }

    [Test]
    public void Contains_MissingKey_ReturnsFalse()
    {
        Assert.IsFalse(registry.Contains(NetProtocolType.Udp, "nope"));
    }

    [Test]
    public void Remove_ExistingKey_ReturnsTrueAndRemoves()
    {
        registry.Add(NetProtocolType.Udp, "U1", MakePort("U1"));
        bool removed = registry.Remove(NetProtocolType.Udp, "U1");

        Assert.IsTrue(removed);
        Assert.IsNull(registry.Get(NetProtocolType.Udp, "U1"));
    }

    [Test]
    public void Remove_MissingKey_ReturnsFalse()
    {
        Assert.IsFalse(registry.Remove(NetProtocolType.TcpServer, "ghost"));
    }

    [Test]
    public void Add_OverwritesExistingKey()
    {
        var first  = MakePort("first");
        var second = MakePort("second");
        registry.Add(NetProtocolType.TcpServer, "key", first);
        registry.Add(NetProtocolType.TcpServer, "key", second);

        Assert.AreSame(second, registry.Get(NetProtocolType.TcpServer, "key"));
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Test]
    public void GetAll_ReturnsAllTypes()
    {
        registry.Add(NetProtocolType.TcpServer, "S1", MakePort("S1"));
        registry.Add(NetProtocolType.TcpClient, "C1", MakePort("C1"));
        registry.Add(NetProtocolType.Udp,       "U1", MakePort("U1"));

        Assert.AreEqual(3, registry.GetAll().Count());
    }

    [Test]
    public void GetAll_ReturnsDefensiveCopy()
    {
        registry.Add(NetProtocolType.TcpServer, "S1", MakePort("S1"));
        var snapshot = registry.GetAll().ToList();
        registry.Add(NetProtocolType.TcpServer, "S2", MakePort("S2"));

        Assert.AreEqual(1, snapshot.Count, "舊快照不應受後續新增影響");
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    [Test]
    public void Clear_RemovesAllEntries()
    {
        registry.Add(NetProtocolType.TcpServer, "S1", MakePort("S1"));
        registry.Add(NetProtocolType.TcpClient, "C1", MakePort("C1"));
        registry.Clear();

        Assert.AreEqual(0, registry.GetAll().Count());
    }

    // ── 型別隔離 ──────────────────────────────────────────────────────────────

    [Test]
    public void DifferentTypes_DoNotInterfere()
    {
        var serverPort = MakePort("server");
        var clientPort = MakePort("client");
        registry.Add(NetProtocolType.TcpServer, "same_key", serverPort);
        registry.Add(NetProtocolType.TcpClient, "same_key", clientPort);

        Assert.AreSame(serverPort, registry.Get(NetProtocolType.TcpServer, "same_key"));
        Assert.AreSame(clientPort, registry.Get(NetProtocolType.TcpClient, "same_key"));
    }

    // ── 執行緒安全 ────────────────────────────────────────────────────────────

    [Test]
    public void Add_Concurrent_AllEntriesPresent()
    {
        const int count = 200;
        Parallel.For(0, count, i =>
            registry.Add(NetProtocolType.TcpServer, $"key{i}", MakePort($"port{i}")));

        Assert.AreEqual(count, registry.GetAll().Count());
    }
}
