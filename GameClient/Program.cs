namespace GameClient;

class Program
{
    static async Task Main(string[] args)
    {
        var port = 6767;
        var game = new Game();
        await game.Run(port);
    }
}