using osu.Framework;
using osu.Framework.Platform;

namespace Gallery
{
    public static class Program
    {
        public static void Main()
        {
            using GameHost host = Host.GetSuitableDesktopHost("OsuXamlGallery");
            host.Run(new GalleryGame());
        }
    }
}
