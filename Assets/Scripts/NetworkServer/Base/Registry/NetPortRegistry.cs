using static NetworkPortManager;
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
    private readonly Dictionary<NetProtocolType, Dictionary<string, PortData>> registry = new()
    {
        { NetProtocolType.TcpServer, new() },
        { NetProtocolType.TcpClient, new() },
        { NetProtocolType.Udp, new() }
    };

    public void Add(NetProtocolType type, string portKey, PortData data)
        => registry[type][portKey] = data;

    public bool Remove(NetProtocolType type, string portKey)
        => registry[type].Remove(portKey);

    public PortData Get(NetProtocolType type, string portKey)
        => registry[type].TryGetValue(portKey, out var data) ? data : null;

    public bool Contains(NetProtocolType type, string portKey)
        => registry[type].ContainsKey(portKey);

    public IEnumerable<PortData> GetAll()
        => registry.Values.SelectMany(dict => dict.Values);

    public void Clear()
    {
        foreach (var dict in registry.Values)
            dict.Clear();
    }
}