using System;
using System.Collections.Generic;
using System.Linq;

namespace QuiverLibrary.Core
{
    public static class QuiverGameNaming
    {
        public static string DisplayName(QuiverApp app)
        {
            return !string.IsNullOrWhiteSpace(app.CustomDisplayName) ? app.CustomDisplayName : app.Name ?? app.FolderName;
        }

        /// <summary>
        /// Playnite name per folder name. Quiver lists some games more than once
        /// (different ports of the same game), so those get the project name added:
        /// "Mario Kart 64 (SpaghettiKart)".
        /// </summary>
        public static Dictionary<string, string> BuildNames(IEnumerable<QuiverApp> apps)
        {
            var list = apps.ToList();
            var duplicated = new HashSet<string>(
                list.GroupBy(DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);

            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in list)
            {
                var name = DisplayName(app);
                if (duplicated.Contains(name) && !string.IsNullOrWhiteSpace(app.Project))
                {
                    name = $"{name} ({app.Project})";
                }

                names[app.FolderName] = name;
            }

            return names;
        }

        /// <summary>GitHub link for "owner/repo" repositories; null for anything else.</summary>
        public static string RepositoryUrl(QuiverApp app)
        {
            if (string.IsNullOrWhiteSpace(app.Repository) || app.Repository.Contains("://"))
            {
                return null;
            }

            return $"https://github.com/{app.Repository.Trim()}";
        }
    }
}
