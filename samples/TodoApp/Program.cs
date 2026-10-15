using osu.Framework;
using osu.Framework.Platform;

namespace TodoApp
{
    public static class Program
    {
        public static void Main()
        {
            using GameHost host = Host.GetSuitableDesktopHost("OsuXamlTodo");
            host.Run(new TodoGame());
        }
    }
}
