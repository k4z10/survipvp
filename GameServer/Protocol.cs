using GameShared;

namespace GameServer;

public readonly struct InboundPacket
{
    public readonly int ConnectionId;
    public readonly IPacket Packet;

    public InboundPacket(int connectionId, IPacket packet)
    {
        ConnectionId = connectionId;
        Packet = packet;
    }
}