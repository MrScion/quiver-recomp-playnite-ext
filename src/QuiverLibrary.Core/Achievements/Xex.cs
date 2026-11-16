using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace QuiverLibrary.Core.Achievements
{
    /// <summary>A resource of a <c>.xex</c> (the achievements' XDBF is the one named after the title id).</summary>
    public class XexResource
    {
        public string Name { get; set; }

        public uint Address { get; set; }

        public uint Size { get; set; }
    }

    /// <summary>
    /// Reads resources out of an Xbox 360 executable (XEX2): its headers, the image's AES-128 encryption with the
    /// public retail (or zero development) key and the "basic" compression. Formats as Xenia reads them
    /// (<c>xenia/cpu/xex_module.cc</c>, <c>xenia/kernel/util/xex2_info.h</c>). The "normal" (LZX) compression is not
    /// supported yet.
    /// </summary>
    public static class Xex
    {
        public const ushort EncryptionNone = 0;
        public const ushort EncryptionNormal = 1;
        public const ushort CompressionNone = 0;
        public const ushort CompressionBasic = 1;
        public const ushort CompressionNormal = 2;

        private const uint KeyResourceInfo = 0x000002FF;
        private const uint KeyFileFormatInfo = 0x000003FF;
        private const uint KeyImageBaseAddress = 0x00010201;

        private static readonly byte[] RetailKey =
        {
            0x20, 0xB1, 0x85, 0xA5, 0x9D, 0x28, 0xFD, 0xC3, 0x40, 0x58, 0x3F, 0xBB, 0x08, 0x96, 0xBF, 0x91,
        };

        private static readonly byte[] DevKitKey = new byte[16];

        public class Header
        {
            public uint HeaderSize { get; set; }

            public uint SecurityOffset { get; set; }

            public uint BaseAddress { get; set; }

            public ushort Encryption { get; set; }

            public ushort Compression { get; set; }

            /// <summary>Basic compression: (bytes in the file, zero bytes after them) for each block.</summary>
            public List<KeyValuePair<uint, uint>> BasicBlocks { get; } = new List<KeyValuePair<uint, uint>>();

            public List<XexResource> Resources { get; } = new List<XexResource>();

            public byte[] EncryptedKey { get; set; }
        }

        /// <summary>The XEX2 headers; throws <see cref="InvalidDataException"/> when the file isn't one.</summary>
        public static Header ReadHeader(byte[] file)
        {
            if (file.Length < 24 || Encoding.ASCII.GetString(file, 0, 4) != "XEX2")
            {
                throw new InvalidDataException("Not an XEX2 file.");
            }

            var header = new Header
            {
                HeaderSize = U32(file, 0x8),
                SecurityOffset = U32(file, 0x10),
            };
            var count = U32(file, 0x14);
            uint? baseAddress = null;
            for (var i = 0; i < count; i++)
            {
                var at = 0x18 + i * 8;
                var key = U32(file, at);
                var value = U32(file, at + 4);
                switch (key)
                {
                    case KeyImageBaseAddress:
                        baseAddress = value;
                        break;
                    case KeyFileFormatInfo:
                        var infoSize = U32(file, (int)value);
                        header.Encryption = U16(file, (int)value + 4);
                        header.Compression = U16(file, (int)value + 6);
                        if (header.Compression == CompressionBasic)
                        {
                            for (var b = 0; b < (infoSize - 8) / 8; b++)
                            {
                                var block = (int)value + 8 + b * 8;
                                header.BasicBlocks.Add(new KeyValuePair<uint, uint>(U32(file, block), U32(file, block + 4)));
                            }
                        }

                        break;
                    case KeyResourceInfo:
                        var size = U32(file, (int)value);
                        for (var r = 0; r < (size - 4) / 16; r++)
                        {
                            var entry = (int)value + 4 + r * 16;
                            header.Resources.Add(new XexResource
                            {
                                Name = Encoding.ASCII.GetString(file, entry, 8).TrimEnd('\0'),
                                Address = U32(file, entry + 8),
                                Size = U32(file, entry + 12),
                            });
                        }

                        break;
                }
            }

            var security = (int)header.SecurityOffset;
            header.BaseAddress = baseAddress ?? U32(file, security + 0x110);
            header.EncryptedKey = new byte[16];
            Array.Copy(file, security + 0x150, header.EncryptedKey, 0, 16);
            return header;
        }

        /// <summary>
        /// The named resource's bytes (case-insensitive name); null when there is no such resource. Throws
        /// <see cref="NotSupportedException"/> for the LZX compression and <see cref="InvalidDataException"/> when the
        /// image can't be read.
        /// </summary>
        public static byte[] ReadResource(byte[] file, string name)
        {
            var header = ReadHeader(file);
            var resource = header.Resources.Find(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (resource == null)
            {
                return null;
            }

            if (header.Compression != CompressionNone && header.Compression != CompressionBasic)
            {
                throw new NotSupportedException($"XEX compression {header.Compression} (LZX) is not supported yet.");
            }

            var start = resource.Address - header.BaseAddress;
            foreach (var key in header.Encryption == EncryptionNone ? new byte[][] { null } : new[] { RetailKey, DevKitKey })
            {
                var image = ReadImage(file, header, key);
                // A wrong key gives noise: the image is a PE ("MZ").
                if (image.Length >= 2 && image[0] == (byte)'M' && image[1] == (byte)'Z' && start + resource.Size <= image.Length)
                {
                    var bytes = new byte[resource.Size];
                    Array.Copy(image, start, bytes, 0, resource.Size);
                    return bytes;
                }
            }

            throw new InvalidDataException("The XEX image could not be decrypted.");
        }

        /// <summary>The loaded image: decrypted (with <paramref name="key"/> when encrypted) and with the zero runs back.</summary>
        private static byte[] ReadImage(byte[] file, Header header, byte[] key)
        {
            var dataLength = file.Length - (int)header.HeaderSize;
            var data = new byte[dataLength];
            Array.Copy(file, header.HeaderSize, data, 0, dataLength);
            if (key != null)
            {
                data = Decrypt(SessionKey(key, header.EncryptedKey), data);
            }

            if (header.Compression == CompressionNone)
            {
                return data;
            }

            long total = 0;
            foreach (var block in header.BasicBlocks)
            {
                total += block.Key + block.Value;
            }

            var image = new byte[total];
            long read = 0, written = 0;
            foreach (var block in header.BasicBlocks)
            {
                var copy = (int)Math.Min(block.Key, Math.Max(0, data.Length - read));
                Array.Copy(data, read, image, written, copy);
                read += block.Key;
                written += block.Key + block.Value;
            }

            return image;
        }

        /// <summary>The image's key: the header's key decrypted with the console key (AES-128, one block).</summary>
        private static byte[] SessionKey(byte[] consoleKey, byte[] encryptedKey)
        {
            return Decrypt(consoleKey, encryptedKey);
        }

        /// <summary>AES-128-CBC with a zero IV and no padding over whole blocks (a partial last block is left as it is).</summary>
        private static byte[] Decrypt(byte[] key, byte[] data)
        {
            var result = (byte[])data.Clone();
            var whole = data.Length / 16 * 16;
            if (whole == 0)
            {
                return result;
            }

            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                aes.IV = new byte[16];
                using (var decryptor = aes.CreateDecryptor())
                {
                    decryptor.TransformBlock(data, 0, whole, result, 0);
                }
            }

            return result;
        }

        internal static uint U32(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);

        internal static ushort U16(byte[] b, int at) => (ushort)(b[at] << 8 | b[at + 1]);
    }
}
