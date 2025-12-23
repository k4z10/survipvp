using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;

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
        while (_reader.TryRead(out var packet))
        {
            switch (packet.OpCode)
            {
                case OpCode.Disconnect:
                    lock (_lock)
                    {
                        if (_clients.Remove(packet.ConnectionId))
                        {
                            _players.Remove(packet.ConnectionId);
                            Console.WriteLine($"{packet.ConnectionId} disconnected");
                        }
                    }

                    break;
                case OpCode.PlayerMove:
                    HandleMove(packet);
                    break;
            }
        }
    }

    private void HandleMove(InboundPacket packet)
    {
        if (packet.Payload.Length < 8) return;
        float x = BitConverter.ToSingle(packet.Payload, 0);
        float y = BitConverter.ToSingle(packet.Payload, 4);
        
        lock (_lock)
        {
            if (_players.TryGetValue(packet.ConnectionId, out var player))
            {
                player.X = x;
                player.Y = y;
                _players[packet.ConnectionId] = player;
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
        }
    }

    private void BrodcastWorldState()
    {
        lock (_lock)
        {
            int playersCount = _players.Count;
            if (playersCount == 0) return;

            int packetSize = 1 + 8 + 4 + playersCount * Marshal.SizeOf<PlayerState>();
            byte[] buffer = new byte[packetSize];
            buffer[0] = (byte)OpCode.WorldUpdate;
            
            long timestamp = Stopwatch.GetTimestamp();
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(1), timestamp);
            
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(9), playersCount);

            int offset = 13;
            foreach (var player in _players.Values)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), player.Id);
                BitConverter.TryWriteBytes(buffer.AsSpan(offset + 4), player.X);
                BitConverter.TryWriteBytes(buffer.AsSpan(offset + 8), player.Y);
                offset += 12;
            }

            foreach (var client in _clients.Values)
            {
                client.Send(buffer);
            }
        }
    }

    public void AddClient(ClientSession session)
    {
        lock (_lock)
        {
            _clients[session.Id] = session;
            _players[session.Id] = new PlayerState { Id = session.Id, X = 250, Y = 250 };
        }
        Console.WriteLine($"Client {session.Id} joined game");
    }
}