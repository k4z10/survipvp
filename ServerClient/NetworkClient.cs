using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;

namespace GameClient;

public class NetworkClient
{
    private TcpClient _client;
    private NetworkStream _stream;
    private CancellationTokenSource _cts = new();

    public Dictionary<int, PlayerState> WorldState { get; private set; } = new();
    public int MyPlayerId { get; private set; } = -1;
    public bool IsConnected => _client != null && _client.Connected;

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
                     result[kvp.Key] = new PlayerState { Id = kvp.Key, X = lx, Y = ly };
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

        // Packet: [Len 4][OpCode 1][X 4][Y 4]
        int payloadSize = 9; 
        byte[] buffer = new byte[4 + payloadSize];

        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0), payloadSize);
        buffer[4] = (byte)OpCode.PlayerMove; // 0x02
        BitConverter.TryWriteBytes(buffer.AsSpan(5), x);
        BitConverter.TryWriteBytes(buffer.AsSpan(9), y);

        try { _stream.Write(buffer); } catch { _cts.Cancel(); }
    }

    private async Task ReceiveLoop()
    {
        // Bufor na nagłówek (długość)
        byte[] lenBuffer = new byte[4];

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // 1. Czytaj długość (4 bajty)
                if (!await ReadExactAsync(lenBuffer, 4)) break;
                int length = BinaryPrimitives.ReadInt32LittleEndian(lenBuffer);

                // 2. Czytaj payload
                byte[] payload = new byte[length];
                if (!await ReadExactAsync(payload, length)) break;

                // 3. Przetwórz
                ProcessPacket(payload);
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

    private void ProcessPacket(byte[] data)
    {
        var opCode = (OpCode)data[0];

        switch (opCode)
        {
            case OpCode.Connect: // Handshake (0x01)
                // Payload: [OpCode 1b][ID 4b]
                MyPlayerId = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(1));
                Console.WriteLine($"Assigned Player ID: {MyPlayerId}");
                break;

            case OpCode.WorldUpdate: // Snapshot (0x03)
                // Payload: [OpCode 1b][Timestamp 8b][Count 4b][ID 4b][X 4b][Y 4b]...
                long serverTimestamp = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(1));
                int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(9));
                
                // Sync Time
                long now = Stopwatch.GetTimestamp();
                long latency = 0; // Assume 0 or calc later
                long offset = now - serverTimestamp + latency;
                
                if (!_timeSynced)
                {
                    _serverTimeOffset = offset;
                    _timeSynced = true;
                }
                else
                {
                    // Smooth sync (simple exponential smoothing)
                    _serverTimeOffset = (long)(_serverTimeOffset * 0.9 + offset * 0.1);
                }

                var newPlayers = new Dictionary<int, PlayerState>(count);
                int offsetIdx = 13;

                for (int i = 0; i < count; i++)
                {
                    if (offsetIdx + 12 > data.Length) break;

                    int id = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offsetIdx));
                    float x = BitConverter.ToSingle(data.AsSpan(offsetIdx + 4));
                    float y = BitConverter.ToSingle(data.AsSpan(offsetIdx + 8));

                    newPlayers[id] = new PlayerState { Id = id, X = x, Y = y };
                    offsetIdx += 12;
                }

                lock (_snapshots)
                {
                    _snapshots.Add(new WorldSnapshot { Timestamp = serverTimestamp, Players = newPlayers });
                    // Keep buffer small? Let GetInterpolatedState prune.
                }
                break;
        }
    }
}