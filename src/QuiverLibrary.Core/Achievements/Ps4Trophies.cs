using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace QuiverLibrary.Core.Achievements
{
    /// <summary>
    /// The trophies of a shadPS4 based port that keeps them its own way (Bloodborne, seen in a played
    /// install): <c>trophies.txt</c> with one <c>&lt;id&gt; &lt;unix time&gt;</c> line per unlocked trophy,
    /// and beside it <c>trophy_cache\metadata.json</c> (every trophy) and <c>trophy_cache\icons\TROPnnn.PNG</c>.
    /// </summary>
    public static class Ps4Trophies
    {
        [DataContract]
        public class Metadata
        {
            [DataMember(Name = "entries")]
            public List<MetadataEntry> Entries { get; set; }
        }

        [DataContract]
        public class MetadataEntry
        {
            [DataMember(Name = "id")]
            public int Id { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "description")]
            public string Description { get; set; }

            /// <summary>P, G, S or B.</summary>
            [DataMember(Name = "grade")]
            public string Grade { get; set; }

            [DataMember(Name = "hidden")]
            public bool Hidden { get; set; }
        }

        /// <param name="unlockedFile">The port's <c>trophies.txt</c>.</param>
        /// <returns>Every trophy from the metadata, or only the unlocked ones (by id) when there is none.</returns>
        public static List<RecompAchievement> Read(string unlockedFile)
        {
            var unlocked = ReadUnlocked(unlockedFile);
            var cache = Path.Combine(Path.GetDirectoryName(unlockedFile), "trophy_cache");
            QuiverJson.TryReadFile<Metadata>(Path.Combine(cache, "metadata.json"), out var metadata);
            var entries = metadata?.Entries?.Where(e => e != null).ToList();
            if (entries == null || entries.Count == 0)
            {
                entries = unlocked.Keys.Select(id => new MetadataEntry { Id = id }).ToList();
            }

            return entries
                .OrderBy(e => e.Id)
                .Select(e =>
                {
                    var icon = Path.Combine(cache, "icons", "TROP" + e.Id.ToString("000", CultureInfo.InvariantCulture) + ".PNG");
                    var isUnlocked = unlocked.TryGetValue(e.Id, out var time);
                    return new RecompAchievement
                    {
                        Id = e.Id.ToString(CultureInfo.InvariantCulture),
                        Name = string.IsNullOrWhiteSpace(e.Name) ? "Trophy " + e.Id.ToString(CultureInfo.InvariantCulture) : e.Name,
                        Description = e.Description,
                        TrophyType = TrophyType(e.Grade),
                        Hidden = e.Hidden,
                        Unlocked = isUnlocked,
                        UnlockTime = isUnlocked ? time : null,
                        IconPath = File.Exists(icon) ? icon : null,
                    };
                })
                .ToList();
        }

        /// <summary>Unlock times (local) by trophy id; a line without a valid time counts as unlocked with no date.</summary>
        public static Dictionary<int, DateTime?> ReadUnlocked(string unlockedFile)
        {
            var result = new Dictionary<int, DateTime?>();
            foreach (var line in File.ReadAllLines(unlockedFile))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    continue;
                }

                DateTime? time = null;
                if (parts.Length > 1 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && unix > 0)
                {
                    time = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
                }

                result[id] = time;
            }

            return result;
        }

        private static string TrophyType(string grade)
        {
            switch ((grade ?? "").Trim().ToUpperInvariant())
            {
                case "P": return "platinum";
                case "G": return "gold";
                case "S": return "silver";
                case "B": return "bronze";
                default: return null;
            }
        }
    }
}
