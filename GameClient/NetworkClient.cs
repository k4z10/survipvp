using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using GameShared;
using MemoryPack;

namespace GameClient;

public class NetworkClient
{
    private TcpClient _client;
    private NetworkStream _stream;
    private CancellationTokenSource _cts = new();

    public Dictionary<int, PlayerState> WorldState { get; private set; } = new();
    public Dictionary<int, ResourceState> Resources { get; private set; } = new();
    public Dictionary<int, StructureState> Structures { get; private set; } = new();
    public Dictionary<ResourceType, int> Inventory { get; private set; } = new();
    public HashSet<WeaponType> UnlockedWeapons { get; private set; } = new();
    public int MyPlayerId { get; private set; } = -1;
    public bool IsConnected => _client != null && _client.Connected;
    public event Action<int> OnDeath; 


    private readonly List<WorldSnapshot> _snapshots = new();
    private long _serverTimeOffset = 0;
    private bool _timeSynced = false;
    
    public struct WorldSnapshot
    {
        public long Timestamp;
        public Dictionary<int, PlayerState> Players;
    }

    public Dictionary<int, PlayerState> GetInterpolatedState(float interpolationDelaySeconds)
    {
         long delayTicks = (long)(interpolationDelaySeconds * Stopwatch.Frequency);
         long renderTime = Stopwatch.GetTimestamp() - _serverTimeOffset - delayTicks;

         lock (_snapshots)
         {
             // 1. Remove too old snapshots
             while (_snapshots.Count > 2 && _snapshots[1].Timestamp < renderTime)
             {
                 _snapshots.RemoveAt(0);
             }

             if (_snapshots.Count == 0) return new Dictionary<int, PlayerState>();
             
             // 2. If we only have one, or renderTime is newer than newest (lag?), return newest
             if (_snapshots.Count == 1 || renderTime >= _snapshots.Last().Timestamp)
             {
                 return _snapshots.Last().Players;
             }
             
             // 3. Find A and B
             WorldSnapshot prev = _snapshots[0];
             WorldSnapshot next = _snapshots[1];
             
             for (int i = 0; i < _snapshots.Count - 1; i++)
             {
                 if (_snapshots[i].Timestamp <= renderTime && _snapshots[i+1].Timestamp >= renderTime)
                 {
                     prev = _snapshots[i];
                     next = _snapshots[i+1];
                     break;
                 }
             }
             
             // 4. Interpolate
             double total = next.Timestamp - prev.Timestamp;
             double current = renderTime - prev.Timestamp;
             float t = (float)(current / total);

             var result = new Dictionary<int, PlayerState>();
             foreach (var kvp in next.Players)
             {
                 if (prev.Players.TryGetValue(kvp.Key, out var pPrev))
                 {
                     // Lerp
                     float lx = pPrev.X + (kvp.Value.X - pPrev.X) * t;
                     float ly = pPrev.Y + (kvp.Value.Y - pPrev.Y) * t;
                     result[kvp.Key] = new PlayerState { 
                         Id = kvp.Key, 
                         X = lx, 
                         Y = ly, 
                         CurrentWeapon = kvp.Value.CurrentWeapon,
                         Rotation = kvp.Value.Rotation,
                         HP = kvp.Value.HP,
                         Nickname = kvp.Value.Nickname,
                         Color = kvp.Value.Color
                     };
                 }
                 else
                 {
                     // New player, snap
                     result[kvp.Key] = kvp.Value;
                 }
             }
             return result;
         }
    }

    public async Task ConnectAsync(string ip, int port)
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(ip, port);
            _client.NoDelay = true;
            _stream = _client.GetStream();
            Console.WriteLine("Connected to server.");

            _ = Task.Run(ReceiveLoop);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection failed: {ex.Message}");
        }
    }

    public void SendPosition(float x, float y)
    {
        if (!IsConnected) return;

        var packet = new PlayerMovePacket(x, y);
        Send(packet);
    }
    
    public void SendJoinRequest(string nickname, uint color)
    {
        if (!IsConnected) return;
        Send(new JoinRequestPacket(nickname, color));
    }
    
    public void SendGather(int resourceId)
    {
        if (!IsConnected) return;
        Send(new GatherResourcePacket(resourceId));
    }

    public void SendCraft(WeaponType weapon)
    {
        if (!IsConnected) return;
        Send(new CraftRequestPacket(weapon));
    }

    public void SendEquip(WeaponType weapon)
    {
        if (!IsConnected) return;
        Send(new EquipRequestPacket(weapon));
    }
    
    public void SendAttack()
    {
        if (!IsConnected) return;
        Send(new AttackPacket());
    }

    public void SendRotate(float rotation)
    {
        if (!IsConnected) return;
        Send(new PlayerRotatePacket(rotation));
    }
    
    public void SendBuild(StructureType type, float x, float y, float rotation)
    {
        if (!IsConnected) return;
        Send(new BuildRequestPacket(type, x, y, rotation));
    }
    
    private void Send(IPacket packet)
    {
        byte[] data = MemoryPackSerializer.Serialize(packet);
        int totalLength = data.Length;
        
        byte[] lenRaw = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenRaw, totalLength);
        
        try 
        { 
            lock (_stream)
            {
                _stream.Write(lenRaw);
                _stream.Write(data); 
            }
        } 
        catch { _cts.Cancel(); }
    }

    private async Task ReceiveLoop()
    {
        byte[] lenBuffer = new byte[4];

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // 1. Read length
                if (!await ReadExactAsync(lenBuffer, 4)) break;
                int length = BinaryPrimitives.ReadInt32LittleEndian(lenBuffer);

                // 2. Read payload
                byte[] payload = new byte[length];
                if (!await ReadExactAsync(payload, length)) break;

                // 3. Process
                // Deserialize IPacket
                var packet = MemoryPackSerializer.Deserialize<IPacket>(payload);
                ProcessPacket(packet);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Disconnected: {ex.Message}");
        }
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = await _stream.ReadAsync(buffer, offset, count - offset, _cts.Token);
            if (read == 0) return false; // Socket closed
            offset += read;
        }
        return true;
    }

    private void ProcessPacket(IPacket packet)
    {
        switch (packet)
        {
            case JoinPacket join:
                MyPlayerId = join.PlayerId;
                Console.WriteLine($"Assigned Player ID: {MyPlayerId}");
                break;

            case WorldUpdatePacket update:
                long serverTimestamp = update.Timestamp;
                
                // Sync Time
                long now = Stopwatch.GetTimestamp();
                long latency = 0; 
                long offset = now - serverTimestamp + latency;
                
                if (!_timeSynced)
                {
                    _serverTimeOffset = offset;
                    _timeSynced = true;
                }
                else
                {
                    _serverTimeOffset = (long)(_serverTimeOffset * 0.9 + offset * 0.1);
                }

                var newPlayers = new Dictionary<int, PlayerState>();
                foreach (var p in update.Players)
                {
                    newPlayers[p.Id] = p;
                }

                lock (_snapshots)
                {
                    _snapshots.Add(new WorldSnapshot { Timestamp = serverTimestamp, Players = newPlayers });
                }
                break;

            case ResourceStatePacket res:
                foreach (var r in res.Resources)
                {
                    Resources[r.Id] = r;
                }
                break;

            case InventoryUpdatePacket inv:
                Inventory = inv.Inventory;
                UnlockedWeapons = new HashSet<WeaponType>(inv.UnlockedWeapons);
                break;
                
            case StructureStatePacket str:
                var newStructures = new Dictionary<int, StructureState>();
                foreach (var s in str.Structures)
                {
                    newStructures[s.Id] = s;
                }
                Structures = newStructures;
                break;
                
            case DeathPacket death:
                OnDeath?.Invoke(death.RespawnTime);
                break;
        }
    }
}