using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QuiverLibrary.Core.Ludusavi
{
    /// <summary>A game for Ludusavi: the name Playnite (and so the Ludusavi extension) knows it by, and its save folders.</summary>
    public class LudusaviGame
    {
        public string Name { get; set; }

        public List<string> Folders { get; set; } = new List<string>();
    }

    /// <summary>
    /// A Ludusavi secondary manifest (same format as Ludusavi's own <c>manifest.yaml</c>) describing
    /// where the Quiver apps keep their saves, so Ludusavi recognizes them by their Playnite name.
    /// A folder backs up everything inside it.
    /// </summary>
    public static class LudusaviManifest
    {
        public const string FileName = "ludusavi-recomps.yaml";

        public static string Build(IEnumerable<LudusaviGame> games)
        {
            var text = new StringBuilder();
            text.Append("---\n");
            text.Append("# Written by the Quiver Library extension for Playnite on every library update; changes are overwritten.\n");
            foreach (var game in games
                .Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.Folders != null && g.Folders.Count > 0)
                .GroupBy(g => g.Name, StringComparer.Ordinal)
                .Select(g => new LudusaviGame { Name = g.Key, Folders = g.SelectMany(x => x.Folders).ToList() })
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            {
                text.Append(Quote(game.Name)).Append(":\n");
                text.Append("  files:\n");
                foreach (var folder in game.Folders.Select(GlobPath).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    text.Append("    ").Append(Quote(folder)).Append(":\n");
                    text.Append("      tags:\n");
                    text.Append("        - save\n");
                    text.Append("      when:\n");
                    text.Append("        - os: windows\n");
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// Forward slashes, and the glob characters escaped Ludusavi's way (<c>[</c> becomes <c>[[]</c>).
        /// <c>*</c> and <c>?</c> stay: Windows paths can't contain them, so they're the database's own patterns.
        /// </summary>
        public static string GlobPath(string path)
        {
            var result = new StringBuilder();
            foreach (var c in path.Replace('\\', '/').TrimEnd('/'))
            {
                if (c == '[' || c == ']' || c == '{' || c == '}')
                {
                    result.Append('[').Append(c).Append(']');
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }

        /// <summary>A YAML single-quoted scalar: only the quote itself needs escaping (doubled).</summary>
        private static string Quote(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
