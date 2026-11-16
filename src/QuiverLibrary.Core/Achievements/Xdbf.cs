using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QuiverLibrary.Core.Achievements
{
    /// <summary>An achievement as a game's XDBF (its "SPA" data) defines it.</summary>
    public class SpaAchievement
    {
        public int Id { get; set; }

        public string Name { get; set; }

        /// <summary>Shown once unlocked.</summary>
        public string Description { get; set; }

        /// <summary>Shown while locked (often the same text, or a hint).</summary>
        public string LockedDescription { get; set; }

        public int Gamerscore { get; set; }

        /// <summary>The XDBF image (section 2) with its icon.</summary>
        public uint ImageId { get; set; }

        public uint Flags { get; set; }
    }

    /// <summary>
    /// Xbox 360 XDBF data ("SPA" of a game): achievements (XACH), string tables per language (XSTR) and images (PNG). Formats as
    /// Xenia reads them (<c>xenia/kernel/util/xdbf_utils.h/.cc</c>), all big-endian.
    /// </summary>
    public class Xdbf
    {
        public const ushort SectionMetadata = 1;
        public const ushort SectionImage = 2;
        public const ushort SectionStringTable = 3;

        /// <summary>Xbox 360 languages (<c>XLanguage</c>).</summary>
        public const int LanguageEnglish = 1;

        private const ulong IdXach = 0x58414348;
        private const ulong IdXstc = 0x58535443;

        private readonly byte[] data;
        private readonly int content;
        private readonly List<Entry> entries = new List<Entry>();

        private class Entry
        {
            public ushort Section;
            public ulong Id;
            public int Offset;
            public int Size;
        }

        public Xdbf(byte[] data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            if (data.Length < 24 || Encoding.ASCII.GetString(data, 0, 4) != "XDBF")
            {
                throw new InvalidDataException("Not XDBF data.");
            }

            var entryCount = (int)Xex.U32(data, 8);
            var entryUsed = (int)Xex.U32(data, 12);
            var freeCount = (int)Xex.U32(data, 16);
            for (var i = 0; i < entryUsed; i++)
            {
                var at = 24 + i * 18;
                entries.Add(new Entry
                {
                    Section = Xex.U16(data, at),
                    Id = (ulong)Xex.U32(data, at + 2) << 32 | Xex.U32(data, at + 6),
                    Offset = (int)Xex.U32(data, at + 10),
                    Size = (int)Xex.U32(data, at + 14),
                });
            }

            content = 24 + entryCount * 18 + freeCount * 8;
        }

        /// <summary>The bytes of an entry, or null.</summary>
        public byte[] Get(ushort section, ulong id)
        {
            var entry = entries.Find(e => e.Section == section && e.Id == id);
            if (entry == null || content + entry.Offset + entry.Size > data.Length)
            {
                return null;
            }

            var bytes = new byte[entry.Size];
            Array.Copy(data, content + entry.Offset, bytes, 0, entry.Size);
            return bytes;
        }

        /// <summary>The game's default language (XSTC), English when it has none.</summary>
        public int DefaultLanguage
        {
            get
            {
                var xstc = Get(SectionMetadata, IdXstc);
                return xstc != null && xstc.Length >= 16 ? (int)Xex.U32(xstc, 12) : LanguageEnglish;
            }
        }

        /// <summary>The strings of a language, by id; empty when the game doesn't have that language.</summary>
        public Dictionary<int, string> Strings(int language)
        {
            var strings = new Dictionary<int, string>();
            var table = Get(SectionStringTable, (ulong)language);
            if (table == null || table.Length < 14)
            {
                return strings;
            }

            var count = Xex.U16(table, 12);
            var at = 14;
            for (var i = 0; i < count && at + 4 <= table.Length; i++)
            {
                var id = Xex.U16(table, at);
                var length = Xex.U16(table, at + 2);
                at += 4;
                if (at + length > table.Length)
                {
                    break;
                }

                strings[id] = Encoding.UTF8.GetString(table, at, length);
                at += length;
            }

            return strings;
        }

        /// <summary>
        /// The achievements, their texts in <paramref name="language"/> when the game has it, else in its default
        /// language, else in English.
        /// </summary>
        public List<SpaAchievement> Achievements(int language)
        {
            var achievements = new List<SpaAchievement>();
            var xach = Get(SectionMetadata, IdXach);
            if (xach == null || xach.Length < 14)
            {
                return achievements;
            }

            var strings = Strings(language);
            if (strings.Count == 0)
            {
                strings = Strings(DefaultLanguage);
            }

            if (strings.Count == 0)
            {
                strings = Strings(LanguageEnglish);
            }

            string Text(int id) => strings.TryGetValue(id, out var text) ? text : null;

            var count = Xex.U16(xach, 12);
            for (var i = 0; i < count; i++)
            {
                var at = 14 + i * 0x24;
                if (at + 0x24 > xach.Length)
                {
                    break;
                }

                achievements.Add(new SpaAchievement
                {
                    Id = Xex.U16(xach, at),
                    Name = Text(Xex.U16(xach, at + 2)),
                    Description = Text(Xex.U16(xach, at + 4)),
                    LockedDescription = Text(Xex.U16(xach, at + 6)),
                    ImageId = Xex.U32(xach, at + 8),
                    Gamerscore = Xex.U16(xach, at + 12),
                    Flags = Xex.U32(xach, at + 16),
                });
            }

            return achievements;
        }

        /// <summary>An image (PNG), or null.</summary>
        public byte[] Image(uint id) => Get(SectionImage, id);
    }
}
