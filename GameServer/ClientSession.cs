using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading.Channels;

namespace GameServer;

public class ClientSession
{
    public int Id { get; }
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ChannelWriter<InboundPacket> _writer;
    private readonly CancellationTokenSource _cts = new();

    public ClientSession(int id, TcpClient client, ChannelWriter<InboundPacket> writer)
    {
        Id = id;
        _client = client;
        _stream = client.GetStream();
        _writer = writer;
    }

    public async Task StartProcessingAsync()
    {
        var pipe = new Pipe();
        var fillTask = FillPipeAsync(pipe.Writer);
        var readTask = ReadPipeAsync(pipe.Reader);
        await Task.WhenAll(fillTask, readTask);
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
            await _writer.WriteAsync(new InboundPacket(Id, OpCode.Disconnect, Array.Empty<byte>()), _cts.Token);
            _client.Close();
        }
    }
    private bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out InboundPacket packet)
    {
        packet = default;

        if (buffer.Length < 4) return false;

        Span<byte> length = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(length);
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(length);

        if (payloadLength > 1024 * 1024) throw new InvalidDataContractException("Packet to large!");
        if (buffer.Length < 4 + payloadLength) return false;

        var payloadSlice = buffer.Slice(4, payloadLength);
        byte opCodeByte = payloadSlice.FirstSpan[0];
        byte[] payloadData = payloadSlice.Slice(1).ToArray();

        packet = new InboundPacket(Id, (OpCode)opCodeByte, payloadData);
        buffer = buffer.Slice(4 + payloadLength);
        return true;
    }
    public void Send(ReadOnlySpan<byte> data)
    {
        if (!_client.Connected) return;
        
        int totalLength = data.Length;
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, totalLength);
        lock (_stream)
        {
            try
            {
                _stream.Write(header);
                _stream.Write(data);
            }
            catch
            {
                _cts.Cancel();
            }
        }
    }
}