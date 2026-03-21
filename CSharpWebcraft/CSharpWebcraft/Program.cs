using CSharpWebcraft.Core;

namespace CSharpWebcraft;

class Program
{
    static void Main(string[] args)
    {
        using var game = new WebcraftGame();
        game.Run();
    }
}
