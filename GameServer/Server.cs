using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace GameServer;

class Program
{
    static async Task Main(string[] args)
    {
        int port = 5002;
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"Server started on port {port}");

        var channel = Channel.CreateUnbounded<InboundPacket>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });
        var engine = new GameEngine(channel.Reader);
        _ = Task.Run(engine.RunLoopAsync);

        int nextClientId = 1;
        while (true)
        {
            var tcpClient = await listener.AcceptTcpClientAsync();
            tcpClient.NoDelay = true;

            var session = new ClientSession(nextClientId++, tcpClient, channel.Writer);
            
            byte[] handshakePayload = new byte[5];
            handshakePayload[0] = (byte)OpCode.Connect;
            BinaryPrimitives.WriteInt32LittleEndian(handshakePayload.AsSpan(1), session.Id);

            session.Send(handshakePayload);

            engine.AddClient(session);
            _ = Task.Run(session.StartProcessingAsync);
        }
    }
}