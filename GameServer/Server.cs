using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using GameShared;
using MemoryPack;

namespace GameServer;

class Program
{
    static async Task Main(string[] args)
    {
        int port = 6767;
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
            
            // Send Handshake
            session.Send(new JoinPacket(session.Id));

            engine.AddClient(session);
            _ = Task.Run(session.StartProcessingAsync);
        }
    }
}