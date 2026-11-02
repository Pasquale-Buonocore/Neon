using System;
using System.IO;
using System.Web.Script.Serialization;

namespace Neon
{
    public sealed class Preferences
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Background { get; set; }
        public bool Compact { get; set; }
        public string Theme { get; set; }
        public bool DockToCorner { get; set; }
        public double WidgetScale { get; set; }
        public double FontScale { get; set; }
        public double RefreshSeconds { get; set; }
        public Preferences()
        {
            Left = Top = -99999; Background = 0.60; Theme = "matrix"; DockToCorner = true;
            WidgetScale = FontScale = RefreshSeconds = 1;
        }
        public Preferences Copy() { return (Preferences)MemberwiseClone(); }
        public void Normalize()
        {
            Left = Finite(Left) ? Left : -99999; Top = Finite(Top) ? Top : -99999;
            Background = Limit(Background, 0, 0.95, 0.60);
            WidgetScale = Limit(WidgetScale, 0.60, 1.50, 1);
            FontScale = Limit(FontScale, 0.80, 1.40, 1);
            RefreshSeconds = Limit(RefreshSeconds, 0.5, 10, 1);
            Theme = ThemePalette.Find(Theme).Id;
        }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static double Limit(double value, double min, double max, double fallback)
        { return Finite(value) ? Math.Max(min, Math.Min(max, value)) : fallback; }
    }

    internal static class PreferencesStore
    {
        public static Preferences Load(string path)
        {
            try
            {
                var value = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(path)) ?? new Preferences();
                value.Normalize(); return value;
            }
            catch { return new Preferences(); }
        }
        public static bool Save(string path, Preferences preferences)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(preferences));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch { return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
    }
}
