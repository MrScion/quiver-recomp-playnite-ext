using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace QuiverLibrary.Core.Achievements
{
    /// <summary>
    /// The CSV that the Playnite Achievements extension imports as a game's custom achievements
    /// (its editor's CSV import; header and formats from its <c>CustomAchievementCsvFormat</c>, v4.1.1).
    /// Unlock times are local, as it reads them.
    /// </summary>
    public static class PlayniteAchievementsCsv
    {
        public const string Header =
            "ID,Title,Description,Points,Trophy Type,Hidden,Rarity,Category,Progress,Progress Total,Unlocked,Unlock Time,Unlocked Icon,Locked Icon";

        public static string Build(IEnumerable<RecompAchievement> achievements)
        {
            var text = new StringBuilder();
            text.Append(Header).Append("\r\n");
            foreach (var a in achievements.Where(a => a != null))
            {
                var fields = new[]
                {
                    a.Id,
                    a.Name,
                    a.Description,
                    a.Points?.ToString(CultureInfo.InvariantCulture),
                    a.TrophyType,
                    a.Hidden ? "true" : "false",
                    null,
                    null,
                    null,
                    null,
                    a.Unlocked ? "true" : "false",
                    a.Unlocked && a.UnlockTime.HasValue ? a.UnlockTime.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null,
                    a.IconPath,
                    null,
                };
                text.Append(string.Join(",", fields.Select(Escape))).Append("\r\n");
            }

            return text.ToString();
        }

        private static string Escape(string value)
        {
            var safe = value ?? "";
            if (safe.IndexOfAny(new[] { ',', ';', '\t', '"', '\r', '\n' }) < 0)
            {
                return safe;
            }

            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }
    }
}
