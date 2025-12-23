namespace GameClient;

class Program
{
    static async Task Main(string[] args)
    {
        var ip = "127.0.0.1";
        var port = 5002;
        var game = new Game();
        await game.Run(ip, port);
    }
}