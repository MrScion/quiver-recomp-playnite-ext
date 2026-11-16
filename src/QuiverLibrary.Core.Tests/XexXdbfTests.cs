using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuiverLibrary.Core.Achievements;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    /// <summary>Synthetic XEX2/XDBF built the way Xenia reads them (big-endian), encrypted with the retail key.</summary>
    public class XexXdbfTests
    {
        private static readonly byte[] RetailKey =
        {
            0x20, 0xB1, 0x85, 0xA5, 0x9D, 0x28, 0xFD, 0xC3, 0x40, 0x58, 0x3F, 0xBB, 0x08, 0x96, 0xBF, 0x91,
        };

        private static readonly byte[] Png = { 0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3 };

        private static void Be(List<byte> b, uint v) => b.AddRange(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

        private static void Be(List<byte> b, ushort v) => b.AddRange(new[] { (byte)(v >> 8), (byte)v });

        /// <summary>XDBF with two achievements, an English and a Spanish string table and one image.</summary>
        public static byte[] BuildXdbf()
        {
            byte[] Table(string magic, ushort count, Action<List<byte>> body)
            {
                var content = new List<byte>();
                body(content);
                var t = new List<byte>(Encoding.ASCII.GetBytes(magic));
                Be(t, 1u);
                Be(t, (uint)(content.Count + 2));
                Be(t, count);
                t.AddRange(content);
                return t.ToArray();
            }

            byte[] Strings(params string[] texts) => Table("XSTR", (ushort)texts.Length, c =>
            {
                for (var i = 0; i < texts.Length; i++)
                {
                    var bytes = Encoding.UTF8.GetBytes(texts[i]);
                    Be(c, (ushort)(i + 1));
                    Be(c, (ushort)bytes.Length);
                    c.AddRange(bytes);
                }
            });

            var xach = Table("XACH", 2, c =>
            {
                foreach (var a in new[] { new { Id = 55, Label = 1, Desc = 2, Score = 10 }, new { Id = 56, Label = 3, Desc = 4, Score = 20 } })
                {
                    Be(c, (ushort)a.Id);
                    Be(c, (ushort)a.Label);
                    Be(c, (ushort)a.Desc);
                    Be(c, (ushort)a.Desc);
                    Be(c, 7u); // image id
                    Be(c, (ushort)a.Score);
                    Be(c, (ushort)0);
                    Be(c, 0u);
                    c.AddRange(new byte[16]);
                }
            });
            var english = Strings("Parker's luck", "Spider-Man was done.", "Second", "Two.");
            var spanish = Strings("La suerte de Parker", "Spider-Man estaba acabado.", "Segundo", "Dos.");
            var entries = new[]
            {
                new { Section = (ushort)1, Id = 0x58414348UL, Data = xach },
                new { Section = (ushort)3, Id = 1UL, Data = english },
                new { Section = (ushort)3, Id = 5UL, Data = spanish },
                new { Section = (ushort)2, Id = 7UL, Data = Png },
            };

            var x = new List<byte>(Encoding.ASCII.GetBytes("XDBF"));
            Be(x, 0x10000u);
            Be(x, (uint)entries.Length); // entry slots
            Be(x, (uint)entries.Length); // used
            Be(x, 1u);                   // free slots
            Be(x, 0u);
            uint offset = 0;
            foreach (var e in entries)
            {
                Be(x, e.Section);
                Be(x, (uint)(e.Id >> 32));
                Be(x, (uint)e.Id);
                Be(x, offset);
                Be(x, (uint)e.Data.Length);
                offset += (uint)e.Data.Length;
            }

            x.AddRange(new byte[8]); // the free slot
            foreach (var e in entries)
            {
                x.AddRange(e.Data);
            }

            return x.ToArray();
        }

        /// <summary>An encrypted XEX2, basic compression, whose image holds <paramref name="resource"/> at 0x1000.</summary>
        public static byte[] BuildXex(byte[] resource, string resourceName, ushort compression = 1)
        {
            const uint baseAddress = 0x82000000;
            var image = new byte[0x1000 + ((resource.Length + 15) / 16 * 16)];
            image[0] = (byte)'M';
            image[1] = (byte)'Z';
            Array.Copy(resource, 0, image, 0x1000, resource.Length);

            // Basic compression: the first 0x20 bytes, a 0xFE0 run of zeros, then the rest.
            var stored = new List<byte>();
            stored.AddRange(image.Take(0x20));
            stored.AddRange(image.Skip(0x1000));
            var blocks = new[] { new[] { 0x20u, 0xFE0u }, new[] { (uint)(image.Length - 0x1000), 0u } };

            var sessionKey = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
            byte[] Encrypt(byte[] key, byte[] data, CipherMode mode)
            {
                using (var aes = Aes.Create())
                {
                    aes.Mode = mode;
                    aes.Padding = PaddingMode.None;
                    aes.Key = key;
                    aes.IV = new byte[16];
                    using (var enc = aes.CreateEncryptor())
                    {
                        return enc.TransformFinalBlock(data, 0, data.Length);
                    }
                }
            }

            var body = Encrypt(sessionKey, stored.ToArray(), CipherMode.CBC);
            var encryptedKey = Encrypt(RetailKey, sessionKey, CipherMode.ECB);

            const int headerCount = 3;
            const int formatAt = 0x18 + headerCount * 8;
            const int resourcesAt = formatAt + 0x8 + 2 * 8;
            const int securityAt = resourcesAt + 0x4 + 0x10;
            const int headerSize = securityAt + 0x200;
            var h = new List<byte>(Encoding.ASCII.GetBytes("XEX2"));
            Be(h, 0u);
            Be(h, (uint)headerSize);
            Be(h, 0u);
            Be(h, (uint)securityAt);
            Be(h, (uint)headerCount);
            Be(h, 0x000003FFu); Be(h, (uint)formatAt);
            Be(h, 0x000002FFu); Be(h, (uint)resourcesAt);
            Be(h, 0x00010201u); Be(h, baseAddress);
            Be(h, 0x8u + 16); Be(h, (ushort)1); Be(h, compression);
            foreach (var block in blocks)
            {
                Be(h, block[0]);
                Be(h, block[1]);
            }

            Be(h, 4u + 16);
            h.AddRange(Encoding.ASCII.GetBytes(resourceName.PadRight(8, '\0')));
            Be(h, baseAddress + 0x1000);
            Be(h, (uint)resource.Length);
            var security = new byte[0x200];
            Array.Copy(encryptedKey, 0, security, 0x150, 16);
            h.AddRange(security);
            h.AddRange(body);
            return h.ToArray();
        }

        [Fact]
        public void Xex_DecryptsTheBasicCompressedImage_AndReturnsTheResource()
        {
            var xdbf = BuildXdbf();
            var xex = BuildXex(xdbf, "415608B2");

            var header = Xex.ReadHeader(xex);
            Assert.Equal(0x82000000u, header.BaseAddress);
            Assert.Equal(Xex.CompressionBasic, header.Compression);
            Assert.Equal(2, header.BasicBlocks.Count);

            Assert.Equal(xdbf, Xex.ReadResource(xex, "415608b2"));
            Assert.Null(Xex.ReadResource(xex, "OTHER"));
            Assert.Throws<NotSupportedException>(() => Xex.ReadResource(BuildXex(xdbf, "415608B2", Xex.CompressionNormal), "415608B2"));
            Assert.Throws<InvalidDataException>(() => Xex.ReadHeader(Encoding.ASCII.GetBytes("not a xex at all, really")));
        }

        [Fact]
        public void Xdbf_ReadsAchievementsInTheLanguageAsked_ElseTheDefault()
        {
            var xdbf = new Xdbf(BuildXdbf());

            var spanish = xdbf.Achievements(5);
            Assert.Equal(new[] { 55, 56 }, spanish.Select(a => a.Id));
            Assert.Equal("La suerte de Parker", spanish[0].Name);
            Assert.Equal("Spider-Man estaba acabado.", spanish[0].Description);
            Assert.Equal(10, spanish[0].Gamerscore);
            Assert.Equal(7u, spanish[0].ImageId);
            Assert.Equal("Parker's luck", xdbf.Achievements(3).First().Name); // German missing: default language (English)
            Assert.Equal(Png, xdbf.Image(7));
            Assert.Null(xdbf.Image(8));
        }

        [Fact]
        public void ReXGlue_TakesNamesAndIconsFromTheGame_AndKeepsThemInTheCache()
        {
            using (var temp = new TempDirectory())
            {
                File.WriteAllBytes(temp.WriteFile(@"App\game\Default.xex", "x"), BuildXex(BuildXdbf(), "415608B2"));
                var toml = temp.WriteFile(@"App\profiles\default\achievements\415608B2.toml", "[unlocked.55]\nfiletime = 134374962935936148\n\n[unlocked.99]\n");
                var options = new AchievementReadOptions { Language = 5, CacheDir = temp.Combine("Cache"), PlaceholderName = "Logro {0}" };

                var achievements = ReXGlueAchievements.Read(toml, temp.Combine("App"), options);

                Assert.Equal(new[] { "55", "56", "99" }, achievements.Select(a => a.Id));
                Assert.Equal("La suerte de Parker", achievements[0].Name);
                Assert.Equal(10, achievements[0].Points);
                Assert.True(achievements[0].Unlocked);
                Assert.False(achievements[1].Unlocked);
                Assert.Equal("Logro 99", achievements[2].Name); // unlocked but not in the game's list
                Assert.Equal(temp.Combine(@"Cache\415608B2\7.png"), achievements[0].IconPath);
                Assert.Equal(Png, File.ReadAllBytes(achievements[0].IconPath));
                Assert.True(File.Exists(temp.Combine(@"Cache\415608B2.xdbf")));

                // Read again from the cache, without the game's program.
                File.Delete(temp.Combine(@"App\game\Default.xex"));
                Assert.Equal("La suerte de Parker", ReXGlueAchievements.Read(toml, temp.Combine("App"), options)[0].Name);
            }
        }
    }
}
