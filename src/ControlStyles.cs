using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace Neon
{
    internal static class ControlStyles
    {
        public static ResourceDictionary Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Neon.ControlStyles"))
                return (ResourceDictionary)XamlReader.Load(stream);
        }
    }
}
