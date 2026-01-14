using MemoryPack;

namespace GameShared;

[MemoryPackable]
[MemoryPackUnion(0, typeof(PlayerMovePacket))]
[MemoryPackUnion(1, typeof(WorldUpdatePacket))]
[MemoryPackUnion(2, typeof(JoinPacket))]
[MemoryPackUnion(3, typeof(DisconnectPacket))]
[MemoryPackUnion(4, typeof(ResourceStatePacket))]
[MemoryPackUnion(5, typeof(GatherResourcePacket))]
[MemoryPackUnion(6, typeof(InventoryUpdatePacket))]
[MemoryPackUnion(7, typeof(CraftRequestPacket))]
[MemoryPackUnion(8, typeof(EquipRequestPacket))]
[MemoryPackUnion(9, typeof(AttackPacket))]
[MemoryPackUnion(10, typeof(PlayerRotatePacket))]
[MemoryPackUnion(11, typeof(BuildRequestPacket))]
[MemoryPackUnion(12, typeof(StructureStatePacket))]
[MemoryPackUnion(13, typeof(JoinRequestPacket))]
[MemoryPackUnion(14, typeof(DeathPacket))]
public partial interface IPacket
{
}

[MemoryPackable]
public partial record PlayerMovePacket(float X, float Y) : IPacket;

[MemoryPackable]
public partial record JoinPacket(int PlayerId) : IPacket;

[MemoryPackable]
public partial record JoinRequestPacket(string Nickname, uint Color) : IPacket;

[MemoryPackable]
public partial record DeathPacket(int RespawnTime) : IPacket;

[MemoryPackable]
public partial record DisconnectPacket() : IPacket;

[MemoryPackable]
public partial record WorldUpdatePacket(long Timestamp, List<PlayerState> Players) : IPacket;

[MemoryPackable]
public partial record ResourceStatePacket(List<ResourceState> Resources) : IPacket;

[MemoryPackable]
public partial record GatherResourcePacket(int ResourceId) : IPacket;

[MemoryPackable]
public partial record InventoryUpdatePacket(Dictionary<ResourceType, int> Inventory, List<WeaponType> UnlockedWeapons) : IPacket;

[MemoryPackable]
public partial record CraftRequestPacket(WeaponType Weapon) : IPacket;

[MemoryPackable]
public partial record EquipRequestPacket(WeaponType Weapon) : IPacket;

[MemoryPackable]
public partial record AttackPacket() : IPacket;

[MemoryPackable]
public partial record PlayerRotatePacket(float Rotation) : IPacket;

[MemoryPackable]
public partial record BuildRequestPacket(StructureType Type, float X, float Y, float Rotation) : IPacket;

[MemoryPackable]
public partial record StructureStatePacket(List<StructureState> Structures) : IPacket;

public enum WeaponType
{
    None,
    WoodSword,
    StoneSword,
    GoldSword,
    Fence
}

public enum StructureType
{
    None,
    Wall // Fence
}

public enum ResourceType
{
    Tree,      // Brown
    Rock,      // Gray
    GoldMine,   // Yellow
    Fence      // Item
}

[MemoryPackable]
public partial struct ResourceState
{
    public int Id;
    public ResourceType Type;
    public float X;
    public float Y;
    public bool IsActive; // ture if ready to gather
}

[MemoryPackable]
public partial struct StructureState
{
    public int Id;
    public StructureType Type;
    public float X;
    public float Y;
    public float Rotation; // 0 or 90 degrees
    public int HP; // default 30
}

[MemoryPackable]
public partial struct PlayerState
{
    public int Id;
    public float X;
    public float Y;
    public float Rotation; // Radians
    public int HP; // 0-100
    public WeaponType CurrentWeapon;
    public string Nickname;
    public uint Color; // 0xRRGGBBAA
}
