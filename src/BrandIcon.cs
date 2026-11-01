using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace Neon
{
    internal static class BrandIcon
    {
        private static Stream Open()
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Neon.Icon");
            if (stream == null) throw new InvalidOperationException("Icona NEON mancante.");
            return stream;
        }
        public static Icon Tray()
        {
            using (var stream = Open())
            using (var icon = new Icon(stream, 16, 16)) return (Icon)icon.Clone();
        }
        public static BitmapSource Window(int size = 32)
        {
            using (var stream = Open())
            {
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                foreach (var frame in decoder.Frames) if (frame.PixelWidth == size) { frame.Freeze(); return frame; }
                decoder.Frames[0].Freeze(); return decoder.Frames[0];
            }
        }
    }
}
