using System.Collections.Generic;
using System.Linq;

public enum NetProtocolType
{
    TcpServer,
    TcpClient,
    Udp
}

public class NetPortRegistry
{
    private readonly object _lock = new();

    private readonly Dictionary<NetProtocolType, Dictionary<string, PortData>> registry = new()
    {
        { NetProtocolType.TcpServer, new() },
        { NetProtocolType.TcpClient, new() },
        { NetProtocolType.Udp, new() }
    };

    public void Add(NetProtocolType type, string portKey, PortData data)
    {
        lock (_lock) registry[type][portKey] = data;
    }

    public bool Remove(NetProtocolType type, string portKey)
    {
        lock (_lock) return registry[type].Remove(portKey);
    }

    public PortData Get(NetProtocolType type, string portKey)
    {
        lock (_lock) return registry[type].TryGetValue(portKey, out var data) ? data : null;
    }

    public bool Contains(NetProtocolType type, string portKey)
    {
        lock (_lock) return registry[type].ContainsKey(portKey);
    }

    public IEnumerable<PortData> GetAll()
    {
        lock (_lock) return registry.Values.SelectMany(dict => dict.Values).ToList();
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var dict in registry.Values)
                dict.Clear();
        }
    }
}