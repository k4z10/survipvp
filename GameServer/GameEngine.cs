using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using GameShared;
using MemoryPack;

namespace GameServer;

public class GameEngine
{
    private readonly ChannelReader<InboundPacket> _reader;
    private readonly Dictionary<int, ClientSession> _clients = new();
    private readonly Dictionary<int, PlayerState> _players = new();
    
    private readonly object _lock = new();

    public GameEngine(ChannelReader<InboundPacket> reader)
    {
        _reader = reader;
        _resourceManager = new ResourceManager();
    }

    public async Task RunLoopAsync()
    {
        const int TargetFps = 60;
        const int TickTime = 10_000_000 / TargetFps;

        Console.WriteLine("Game starting...");

        while (true)
        {
            long startTick = Stopwatch.GetTimestamp();

            ProcessNetworkEvents();
            UpdatePhysics();
            BrodcastWorldState();

            long endTick = Stopwatch.GetTimestamp();
            long elapsedTime = endTick - startTick;
            long waitTicks = TickTime - elapsedTime;
            if (waitTicks > 0) await Task.Delay(TimeSpan.FromTicks(waitTicks));
        }
    }

    private void ProcessNetworkEvents()
    {
        while (_reader.TryRead(out var inbound))
        {
            switch (inbound.Packet)
            {
                case DisconnectPacket:
                    lock (_lock)
                    {
                        if (_clients.Remove(inbound.ConnectionId))
                        {
                            _players.Remove(inbound.ConnectionId);
                            _inventories.Remove(inbound.ConnectionId);
                            _unlockedWeapons.Remove(inbound.ConnectionId);
                            _attackCooldowns.Remove(inbound.ConnectionId);
                            Console.WriteLine($"{inbound.ConnectionId} disconnected");
                        }
                    }
                    break;
                    
                case PlayerMovePacket move:
                    HandleMove(inbound.ConnectionId, move);
                    break;

                case GatherResourcePacket gather:
                    HandleGather(inbound.ConnectionId, gather);
                    break;

                case CraftRequestPacket craft:
                    HandleCraft(inbound.ConnectionId, craft);
                    break;

                case EquipRequestPacket equip:
                    HandleEquip(inbound.ConnectionId, equip);
                    break;

                case PlayerRotatePacket rot:
                    HandleRotate(inbound.ConnectionId, rot);
                    break;
                    
                case AttackPacket attack:
                    HandleAttack(inbound.ConnectionId, attack);
                    break;
                    
                case BuildRequestPacket build:
                    HandleBuild(inbound.ConnectionId, build);
                    break;
                    
                case JoinRequestPacket joinReq:
                    HandleJoinRequest(inbound.ConnectionId, joinReq);
                    break;
            }
        }
    }

    private void HandleMove(int connectionId, PlayerMovePacket packet)
    {
        lock (_lock)
        {
            if (_players.TryGetValue(connectionId, out var player))
            {
                player.X = packet.X;
                player.Y = packet.Y;
                _players[connectionId] = player;
            }
        }
    }

    private void UpdatePhysics()
    {
        const float PlayerRadius = 0.5f;
        const float MinDist = PlayerRadius * 2;
        const float MinDistSq = MinDist * MinDist;
        
        lock (_lock)
        {
            var keys = _players.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                for (int j = i + 1; j < keys.Count; j++)
                {
                    var p1 = _players[keys[i]];
                    var p2 = _players[keys[j]];
                    
                    float dx = p2.X - p1.X;
                    float dy = p2.Y - p1.Y;
                    float distSq = dx*dx + dy*dy;
                    
                    if (distSq < MinDistSq)
                    {
                        float dist = MathF.Sqrt(distSq);
                        float overlap = MinDist - dist;
                        float nx, ny;
                        
                        // Handle exact overlap or very close
                        if (dist < 0.0001f)
                        {
                            nx = 1.0f; 
                            ny = 0.0f;
                            overlap = MinDist; // Force full separation
                        }
                        else
                        {
                            nx = dx / dist;
                            ny = dy / dist;
                        }
                        
                        // Push apart
                        float pushX = nx * overlap * 0.5f;
                        float pushY = ny * overlap * 0.5f;
                        
                        p1.X -= pushX;
                        p1.Y -= pushY;
                        p2.X += pushX;
                        p2.Y += pushY;
                        
                        _players[keys[i]] = p1;
                        _players[keys[j]] = p2;
                    }
                }
            }
        
            
            // Player vs Structure Collision
            foreach (var pKey in keys)
            {
                var p = _players[pKey];
                foreach (var s in _structures.Values)
                {
                    // Structure AABB
                    float sw = (s.Rotation == 0) ? 3.0f : 1.0f; // Width depends on rotation
                    float sh = (s.Rotation == 0) ? 1.0f : 3.0f;
                    
                    // Simple AABB check (Player is treated as box for simplicity against structures)
                    // Or Circle vs Rect
                    
                    // Rectangle center and half-extents
                    float sx = s.X;
                    float sy = s.Y;
                    float hw = sw / 2.0f;
                    float hh = sh / 2.0f;
                    
                    // Closest point on rect to circle center
                    float closeX = Math.Clamp(p.X, sx - hw, sx + hw);
                    float closeY = Math.Clamp(p.Y, sy - hh, sy + hh);
                    
                    float dx = p.X - closeX;
                    float dy = p.Y - closeY;
                    float distSq = dx*dx + dy*dy;
                    
                    if (distSq < PlayerRadius * PlayerRadius)
                    {
                        // Collision!
                        float dist = MathF.Sqrt(distSq);
                        float nx, ny, overlap;

                        if (dist < 0.0001f)
                        {
                            // Player is INSIDE the AABB (center overlap)
                            // Find closest edge to push out
                            float dLeft = MathF.Abs(p.X - (sx - hw));
                            float dRight = MathF.Abs(p.X - (sx + hw));
                            float dTop = MathF.Abs(p.Y - (sy - hh));
                            float dBottom = MathF.Abs(p.Y - (sy + hh));
                            
                            float minD = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));
                            
                            if (Math.Abs(minD - dLeft) < 0.001f)      { nx = -1; ny = 0; overlap = dLeft + PlayerRadius; }
                            else if (Math.Abs(minD - dRight) < 0.001f){ nx = 1; ny = 0; overlap = dRight + PlayerRadius; }
                            else if (Math.Abs(minD - dTop) < 0.001f)  { nx = 0; ny = -1; overlap = dTop + PlayerRadius; }
                            else                                      { nx = 0; ny = 1; overlap = dBottom + PlayerRadius; }
                        }
                        else
                        {
                            // Player is OUTSIDE (touching edge/corner)
                            overlap = PlayerRadius - dist;
                            nx = dx / dist;
                            ny = dy / dist;
                        }
                        
                        p.X += nx * overlap;
                        p.Y += ny * overlap;
                        _players[pKey] = p;
                    }
                }
            }
        }
    }

    private void BrodcastWorldState()
    {
        lock (_lock)
        {
            int playersCount = _players.Count;
            if (playersCount == 0 && _clients.Count == 0) return; // if no clients, no broadcast

            long timestamp = Stopwatch.GetTimestamp();
            var packet = new WorldUpdatePacket(timestamp, _players.Values.ToList());

            foreach (var client in _clients.Values)
            {
                client.Send(packet);
            }
        }
    }

    private readonly Dictionary<int, Dictionary<ResourceType, int>> _inventories = new();
    private readonly Dictionary<int, HashSet<WeaponType>> _unlockedWeapons = new();
    private readonly Dictionary<int, long> _attackCooldowns = new(); // Timestamp of last attack
    private readonly Dictionary<int, StructureState> _structures = new();
    private int _nextStructureId = 1;
    private readonly ResourceManager _resourceManager;

    public void AddClient(ClientSession session)
    {
        lock (_lock)
        {
            _clients[session.Id] = session;
            // Player is NOT created here anymore. Waits for JoinRequest.
            _inventories[session.Id] = new Dictionary<ResourceType, int>();
            _unlockedWeapons[session.Id] = new HashSet<WeaponType>();
        }
        Console.WriteLine($"Client {session.Id} connected (awaiting join)");
        
        // Send initial world state
        var resources = _resourceManager.GetAllResources();
        session.Send(new ResourceStatePacket(resources));
        
        lock (_lock)
        {
            if (_structures.Count > 0)
            {
                session.Send(new StructureStatePacket(_structures.Values.ToList()));
            }
        }
    }
    
    // ... Implement Handlers (Moved from backup) ...
    // Since I can't construct the whole file from memory easily, I'll copy the logic I just wrote in previous steps.
    // I need ResourceManager too.
    
    // Handlers
    private void HandleGather(int connectionId, GatherResourcePacket packet)
    {
        float px, py;
        lock (_lock)
        {
            if (!_players.TryGetValue(connectionId, out var p)) return;
            px = p.X;
            py = p.Y;
        }

        if (_resourceManager.TryGather(packet.ResourceId, px, py, out var type))
        {
            lock (_lock)
            {
                if (_inventories.TryGetValue(connectionId, out var inv))
                {
                    if (!inv.ContainsKey(type)) inv[type] = 0;
                    inv[type]++;
                    
                    SendInventoryUpdate(connectionId);
                }
                
                var update = new ResourceStatePacket(new List<ResourceState> 
                { 
                     new ResourceState { Id = packet.ResourceId, Type = type, X = 0, Y = 0, IsActive = false } 
                });
                
                foreach (var c in _clients.Values)
                {
                    c.Send(update);
                }
            }
        }
    }

    private void HandleRotate(int connectionId, PlayerRotatePacket packet)
    {
        lock (_lock)
        {
            if (_players.TryGetValue(connectionId, out var player))
            {
                player.Rotation = packet.Rotation;
                _players[connectionId] = player;
            }
        }
    }

    private void HandleAttack(int connectionId, AttackPacket packet)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(connectionId, out var attacker)) return;
            
            long now = Stopwatch.GetTimestamp();
            if (_attackCooldowns.TryGetValue(connectionId, out var lastAttack))
            {
                if (now - lastAttack < Stopwatch.Frequency / 2) return; 
            }
            _attackCooldowns[connectionId] = now;
            
            int damage = attacker.CurrentWeapon switch
            {
                WeaponType.WoodSword => 10,
                WeaponType.StoneSword => 20,
                WeaponType.GoldSword => 30,
                _ => 5
            };
            
            float attackRange = 2.0f;
            float attackCone = MathF.PI / 4.0f;

            var candidates = new List<(bool IsPlayer, int Id, float DistSq)>();

            var targets = _players.Keys.ToList();
            foreach (var targetId in targets)
            {
                if (targetId == connectionId) continue;
                
                var target = _players[targetId];
                float dx = target.X - attacker.X;
                float dy = target.Y - attacker.Y;
                float distSq = dx*dx + dy*dy;
                
                if (distSq > attackRange * attackRange) continue;
                
                float angleToTarget = MathF.Atan2(dy, dx);
                float angleDiff = MathF.Abs(AngleDifference(attacker.Rotation, angleToTarget));
                
                if (angleDiff <= attackCone)
                {
                    candidates.Add((true, targetId, distSq));
                }
            }
            
            foreach (var sKey in _structures.Keys)
            {
                var s = _structures[sKey];
                
                float sw = (s.Rotation == 0) ? 3.0f : 1.0f;
                float sh = (s.Rotation == 0) ? 1.0f : 3.0f;
                float hw = sw / 2.0f;
                float hh = sh / 2.0f;

                float closeX = Math.Clamp(attacker.X, s.X - hw, s.X + hw);
                float closeY = Math.Clamp(attacker.Y, s.Y - hh, s.Y + hh);
                
                float dx = attacker.X - closeX;
                float dy = attacker.Y - closeY;
                float distSq = dx*dx + dy*dy;
                
                if (distSq < attackRange * attackRange) 
                {
                    float angleToHit = MathF.Atan2(-dy, -dx);
                    float angleDiff = 0f;
                    if (distSq > 0.01f) 
                    {
                         angleDiff = MathF.Abs(AngleDifference(attacker.Rotation, angleToHit));
                    }
                    
                    if (angleDiff <= attackCone)
                    {
                        candidates.Add((false, sKey, distSq));
                    }
                }
            }
            
            if (candidates.Count > 0)
            {
                candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
                var hit = candidates[0]; 
                
                if (hit.IsPlayer)
                {
                     if (_players.TryGetValue(hit.Id, out var target))
                     {
                         target.HP -= damage;
                         Console.WriteLine($"Player {connectionId} hit {hit.Id} for {damage} dmg. HP: {target.HP}");
                         if (target.HP <= 0)
                         {
                              Console.WriteLine($"Player {hit.Id} died.");
                              _players.Remove(hit.Id); 
                              
                              if (_clients.TryGetValue(hit.Id, out var victimSession))
                              {
                                  victimSession.Send(new DeathPacket(5000)); 
                              }
                         }
                         else
                         {
                             _players[hit.Id] = target;
                         }
                     }
                }
                else
                {
                    if (_structures.TryGetValue(hit.Id, out var s))
                    {
                         s.HP -= damage;
                         _structures[hit.Id] = s;
                         if (s.HP <= 0) { _structures.Remove(hit.Id); BroadcastStructures(); }
                         else BroadcastStructures(); // To update HP
                    }
                }
            }
        }
    }
    
    private void BroadcastStructures()
    {
         if (_structures.Count >= 0)
         {
             var pkt = new StructureStatePacket(_structures.Values.ToList());
             foreach (var c in _clients.Values) c.Send(pkt);
         }
    }

    private float AngleDifference(float a, float b)
    {
        float diff = (b - a + MathF.PI) % (2 * MathF.PI) - MathF.PI;
        return diff < -MathF.PI ? diff + 2 * MathF.PI : diff;
    }
    
    private void HandleCraft(int connectionId, CraftRequestPacket packet)
    {
        lock (_lock)
        {
            if (!_inventories.TryGetValue(connectionId, out var inv)) return;
            if (!_unlockedWeapons.TryGetValue(connectionId, out var unlocked)) return;
            
            if (packet.Weapon != WeaponType.Fence && unlocked.Contains(packet.Weapon)) return; 

            if (!CraftingRecipes.Recipes.TryGetValue(packet.Weapon, out var costs)) return;

            foreach (var cost in costs) if (GetCount(inv, cost.Key) < cost.Value) return; 

            foreach (var cost in costs) RemoveResource(inv, cost.Key, cost.Value);
            
            if (packet.Weapon == WeaponType.Fence)
            {
                if (!inv.ContainsKey(ResourceType.Fence)) inv[ResourceType.Fence] = 0;
                inv[ResourceType.Fence]++;
                 Console.WriteLine($"Client {connectionId} crafted Fence. Total: {inv[ResourceType.Fence]}");
            }
            else
            {
                unlocked.Add(packet.Weapon);
                Console.WriteLine($"Client {connectionId} crafted {packet.Weapon}");
            }
            
            SendInventoryUpdate(connectionId);
        }
    }

    private void HandleEquip(int connectionId, EquipRequestPacket packet)
    {
        lock (_lock)
        {
            if (!_unlockedWeapons.TryGetValue(connectionId, out var unlocked)) return;
            if (!_players.TryGetValue(connectionId, out var player)) return;

            if (packet.Weapon == WeaponType.None || unlocked.Contains(packet.Weapon))
            {
                player.CurrentWeapon = packet.Weapon;
                _players[connectionId] = player;
            }
        }
    }

    private int GetCount(Dictionary<ResourceType, int> inv, ResourceType type)
    {
        return inv.TryGetValue(type, out int count) ? count : 0;
    }

    private void RemoveResource(Dictionary<ResourceType, int> inv, ResourceType type, int amount)
    {
        if (inv.ContainsKey(type))
        {
            inv[type] -= amount;
        }
    }

    private void SendInventoryUpdate(int connectionId)
    {
        if (_clients.TryGetValue(connectionId, out var client) && _inventories.TryGetValue(connectionId, out var inv) && _unlockedWeapons.TryGetValue(connectionId, out var unlocked))
        {
            client.Send(new InventoryUpdatePacket(inv, unlocked.ToList()));
        }
    }
    private void HandleBuild(int connectionId, BuildRequestPacket packet)
    {
        lock (_lock)
        {
            if (!_inventories.TryGetValue(connectionId, out var inv)) return;
            if (GetCount(inv, ResourceType.Fence) < 1) return;

            float w = (packet.Rotation == 0) ? 3.0f : 1.0f;
            float h = (packet.Rotation == 0) ? 1.0f : 3.0f;
            
            float newSx = packet.X;
            float newSy = packet.Y;
            float hw = w / 2.0f;
            float hh = h / 2.0f;
            
            bool overlapsPlayer = false;
            foreach (var p in _players.Values)
            {
                float closeX = Math.Clamp(p.X, newSx - hw, newSx + hw);
                float closeY = Math.Clamp(p.Y, newSy - hh, newSy + hh);
                float dx = p.X - closeX;
                float dy = p.Y - closeY;
                float distSq = dx*dx + dy*dy;
                
                if (distSq < (0.5f + 0.1f) * (0.5f + 0.1f))
                {
                    overlapsPlayer = true;
                    break;
                }
            }
            if (overlapsPlayer) return;

            RemoveResource(inv, ResourceType.Fence, 1);
            SendInventoryUpdate(connectionId);
            
            var structure = new StructureState
            {
                Id = _nextStructureId++,
                Type = packet.Type,
                X = packet.X,
                Y = packet.Y,
                Rotation = packet.Rotation,
                HP = 100 
            };
            _structures[structure.Id] = structure;
            
            var statePacket = new StructureStatePacket(_structures.Values.ToList());
            foreach (var c in _clients.Values)
            {
                c.Send(statePacket);
            }
            
            Console.WriteLine($"Client {connectionId} built {structure.Type} at {structure.X},{structure.Y}");
        }
    }

    private void HandleJoinRequest(int connectionId, JoinRequestPacket packet)
    {
        lock (_lock)
        {
            if (!_clients.ContainsKey(connectionId)) return;
            if (_players.ContainsKey(connectionId)) return; 
            
            float angle = Random.Shared.NextSingle() * MathF.PI * 2;
            float dist = Random.Shared.NextSingle() * 20.0f;
            float x = 250 + MathF.Cos(angle) * dist;
            float y = 250 + MathF.Sin(angle) * dist;
            
            var p = new PlayerState
            {
                Id = connectionId,
                X = x,
                Y = y,
                HP = 100,
                Rotation = 0,
                CurrentWeapon = WeaponType.None,
                Nickname = packet.Nickname,
                Color = packet.Color
            };
            
            _players[connectionId] = p;
            
            _clients[connectionId].Send(new JoinPacket(connectionId));
            Console.WriteLine($"Player {packet.Nickname} joined at {x},{y}");
        }
    }}
