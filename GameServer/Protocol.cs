using System.Runtime.InteropServices;
namespace GameServer;

public enum OpCode : byte
{
    Connect = 0x01,
    Disconnect = 0xFF,
    PlayerMove = 0x02,
    WorldUpdate = 0x03
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerState
{
    public int Id;
    public float X;
    public float Y;
}

public readonly struct InboundPacket
{
    public readonly int ConnectionId;
    public readonly OpCode OpCode;
    public readonly byte[] Payload;

    public InboundPacket(int connectionId, OpCode opCode, byte[] payload)
    {
        ConnectionId = connectionId;
        OpCode = opCode;
        Payload = payload;
    }
}