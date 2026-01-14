using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading.Channels;
using GameShared;
using MemoryPack;

namespace GameServer;

public class ClientSession
{
    public int Id { get; }
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ChannelWriter<InboundPacket> _writer;
    private readonly Channel<IPacket> _outbox;
    private readonly CancellationTokenSource _cts = new();

    public ClientSession(int id, TcpClient client, ChannelWriter<InboundPacket> writer)
    {
        Id = id;
        _client = client;
        _stream = client.GetStream();
        _writer = writer;
        _outbox = Channel.CreateUnbounded<IPacket>(new UnboundedChannelOptions 
            { SingleReader = true, SingleWriter = false });
    }

    public async Task StartProcessingAsync()
    {
        var pipe = new Pipe();
        var fillTask = FillPipeAsync(pipe.Writer);
        var readTask = ReadPipeAsync(pipe.Reader);
        var sendTask = SendLoopAsync();
        
        await Task.WhenAny(fillTask, readTask, sendTask);
        _cts.Cancel(); // If any loop finishes (error/close), cancel others
    }

    private async Task FillPipeAsync(PipeWriter writer)
    {
        const int bufferSize = 1024;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                Memory<byte> memory = writer.GetMemory(bufferSize);
                int bytesRead = await _stream.ReadAsync(memory, _cts.Token);

                if (bytesRead == 0) break;

                writer.Advance(bytesRead);
                FlushResult result = await writer.FlushAsync();
                if (result.IsCompleted) break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            throw new Exception($"Error while processing inbound packet. {e.Message}", e);
        }
        finally
        {
            await writer.CompleteAsync();
        }
    }

    private async Task ReadPipeAsync(PipeReader reader)
    {
        try
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;
                while (TryReadFrame(ref buffer, out var packet))
                {
                    await _writer.WriteAsync(packet, _cts.Token);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }
        }
        finally
        {
            await reader.CompleteAsync();
            await _writer.WriteAsync(new InboundPacket(Id, new DisconnectPacket()), _cts.Token);

            _client.Close();
        }
    }
    private bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out InboundPacket packet)
    {
        packet = default;

        if (buffer.Length < 4) return false;

        Span<byte> lengthSpan = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(lengthSpan);
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthSpan);

        if (payloadLength > 1024 * 1024) throw new InvalidDataContractException("Packet too large!");
        if (buffer.Length < 4 + payloadLength) return false;

        var payloadSlice = buffer.Slice(4, payloadLength);
        
        IPacket deserializedPacket;
        try 
        {
             deserializedPacket = MemoryPackSerializer.Deserialize<IPacket>(payloadSlice.ToArray());
        }
        catch
        {
             // Invalid packet, skip
             buffer = buffer.Slice(4 + payloadLength);
             return true; 
        }

        packet = new InboundPacket(Id, deserializedPacket);
        buffer = buffer.Slice(4 + payloadLength);
        return true;
    }
    public void Send(IPacket packet)
    {
        _outbox.Writer.TryWrite(packet);
    }

    private async Task SendLoopAsync()
    {
        try
        {
            var reader = _outbox.Reader;
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out var packet))
                {
                    byte[] data = MemoryPackSerializer.Serialize(packet);
                    int totalLength = data.Length;
                    
                    byte[] header = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(header, totalLength);
                    
                    lock (_stream) // Should be exclusive access now ideally, but lock is safe
                    {
                        _stream.Write(header);
                        _stream.Write(data);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore write errors, connection will close
        }
    }
}