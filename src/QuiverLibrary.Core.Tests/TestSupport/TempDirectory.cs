using System;
using System.IO;
using System.Text;

namespace QuiverLibrary.Core.Tests.TestSupport
{
    /// <summary>Throwaway folder for file-system tests, deleted on dispose.</summary>
    public sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QuiverLibraryTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Combine(params string[] parts)
        {
            var all = new string[parts.Length + 1];
            all[0] = Path;
            parts.CopyTo(all, 1);
            return System.IO.Path.Combine(all);
        }

        /// <summary>Writes a file (creating its folder), UTF-8 with BOM unless told otherwise.</summary>
        public string WriteFile(string relativePath, string content = "", bool bom = true)
        {
            var full = Combine(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            File.WriteAllText(full, content, new UTF8Encoding(bom));
            return full;
        }

        public string CreateFolder(string relativePath)
        {
            var full = Combine(relativePath);
            Directory.CreateDirectory(full);
            return full;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
            }
        }
    }
}
