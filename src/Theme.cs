using System;

namespace Neon
{
    public sealed class ThemePalette
    {
        public string Id, Name, Primary, Secondary, Text, Muted, Warning, Surface, Line;
        public ThemePalette(string id, string name, string primary, string secondary, string text, string muted, string warning, string surface, string line)
        { Id = id; Name = name; Primary = primary; Secondary = secondary; Text = text; Muted = muted; Warning = warning; Surface = surface; Line = line; }
        public static readonly ThemePalette[] All = {
            new ThemePalette("matrix", "Matrix · verde", "#70FFA7", "#73DDEB", "#E3FFED", "#9BBCAB", "#FFC66D", "#050E0A", "#41634E"),
            new ThemePalette("ice", "Ice · azzurro", "#65E4FF", "#8BA9FF", "#E5F8FF", "#9EB8CE", "#FFD080", "#060D19", "#3B5E7D"),
            new ThemePalette("cyberpunk", "Cyberpunk · viola", "#F788FF", "#69F2E5", "#F9E8FF", "#C2A4CB", "#FFD477", "#14091A", "#704575"),
            new ThemePalette("amber", "Amber · arancio", "#FFC16B", "#FFE8A0", "#FFF3DC", "#C4B49C", "#FF8E83", "#160E05", "#75603D"),
            new ThemePalette("crimson", "Crimson · rosso", "#FF8597", "#FFC48C", "#FFE8ED", "#C5A3AF", "#FFDF79", "#18080E", "#784454"),
            new ThemePalette("ghost", "Ghost · bianco", "#E9F1FF", "#AEC6E8", "#F4F7FC", "#AEBACC", "#FFD080", "#0C1119", "#5A697D")
        };
        public static ThemePalette Find(string id)
        {
            foreach (var palette in All) if (String.Equals(id, palette.Id, StringComparison.OrdinalIgnoreCase)) return palette;
            return All[0];
        }
    }

}
