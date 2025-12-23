using System.Runtime.InteropServices;
namespace GameClient;

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