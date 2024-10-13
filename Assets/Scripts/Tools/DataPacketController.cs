using System;
using System.Collections.Generic;
using DevKit.Console;

public class DataPacketController
{
    [Serializable]
    public class DataPacket
    {
        public string dataPacketName = string.Empty;
        public string packetStartMarker = string.Empty;
        public string packetEndMarker = string.Empty;
        public int maxPacketSize = 0;
    }
    readonly Dictionary<string, DataPacket> dataPackets = new();

    public void CreateDataPacket(DataPacket dataPacket)
    {
        if (dataPackets.ContainsKey(dataPacket.dataPacketName))
        {
            ULog.Log("已有相同名稱的遮罩");
            return;
        }

        var packet = new DataPacket()
        {
            dataPacketName = dataPacket.dataPacketName,
            maxPacketSize = dataPacket.maxPacketSize,
            packetStartMarker = dataPacket.packetStartMarker,
            packetEndMarker = dataPacket.packetEndMarker,
        };
        dataPackets.TryAdd(packet.packetStartMarker, packet);
    }
}
