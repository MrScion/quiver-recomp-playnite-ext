using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace QuiverLibrary.Core
{
    /// <summary>Reads Quiver's JSON files with the framework's own serializer (unknown fields are ignored).</summary>
    internal static class QuiverJson
    {
        public static T ReadFile<T>(string path) where T : class
        {
            // FileShare.ReadWrite: Quiver may have the file open while its window is running.
            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                text = reader.ReadToEnd();
            }

            return ReadText<T>(text);
        }

        public static T ReadText<T>(string text) where T : class
        {
            // Re-encoded without a BOM: DataContractJsonSerializer rejects a UTF-8 BOM.
            using (var json = new MemoryStream(new UTF8Encoding(false).GetBytes(text.TrimStart('﻿'))))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(json);
            }
        }

        /// <summary>Indented UTF-8 without a BOM, written to a temporary file first so a failure never leaves half a file.</summary>
        public static void WriteFile<T>(string path, T value) where T : class
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true, "  "))
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(writer, value);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        /// <summary>Never throws: false when the file is missing or not valid JSON.</summary>
        public static bool TryReadFile<T>(string path, out T value) where T : class
        {
            value = null;
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                value = ReadFile<T>(path);
                return value != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
