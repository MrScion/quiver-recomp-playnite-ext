using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace QuiverLibrary.Core.Ludusavi
{
    public enum LudusaviRegistration
    {
        /// <summary>The manifest was already among Ludusavi's secondary manifests.</summary>
        AlreadyRegistered,

        /// <summary>Added; the previous config was kept as <c>config.yaml.bak</c>.</summary>
        Added,

        /// <summary>No <c>config.yaml</c>: Ludusavi isn't installed or hasn't been opened yet.</summary>
        NoConfig,

        /// <summary>The config is laid out in a way this simple editor doesn't change safely.</summary>
        Unrecognized,

        Failed,
    }

    /// <summary>The setting of the Ludusavi extension for Playnite (<c>mtkennerly/ludusavi-playnite</c>) this needs.</summary>
    [DataContract]
    public class LudusaviPlayniteSettings
    {
        /// <summary>The Ludusavi extension's plugin id: its settings are in <c>ExtensionsData\&lt;id&gt;\config.json</c>.</summary>
        public const string PluginId = "72e2de43-d859-44d8-914e-4277741c8208";

        /// <summary>Ludusavi's program; "ludusavi" (the extension's default) means found on PATH.</summary>
        [DataMember(Name = "ExecutablePath")]
        public string ExecutablePath { get; set; }

        /// <summary>The program set in the extension's <c>config.json</c>; null when it can't be read.</summary>
        public static string ReadExecutablePath(string configJson)
        {
            return QuiverJson.TryReadFile<LudusaviPlayniteSettings>(configJson, out var settings) ? settings?.ExecutablePath : null;
        }
    }

    /// <summary>
    /// Ludusavi's <c>config.yaml</c>: where it is and the one change made to it, adding a secondary
    /// manifest (<c>manifest.secondary: - path: ...</c>). Ludusavi has no command to do it, and
    /// rewrites the file in a stable layout (two-space indentation, block lists), which is what
    /// this edits; anything else is left alone and reported as <see cref="LudusaviRegistration.Unrecognized"/>.
    /// </summary>
    public static class LudusaviConfig
    {
        public const string FileName = "config.yaml";

        /// <summary>Next to the program when it's portable (a <c>ludusavi.portable</c> file there), else <c>%APPDATA%\ludusavi</c>.</summary>
        public static string FindConfigPath(string executablePath, string appData, string pathVariable)
        {
            var exe = ResolveExecutable(executablePath, pathVariable);
            if (exe != null)
            {
                var dir = Path.GetDirectoryName(exe);
                if (File.Exists(Path.Combine(dir, "ludusavi.portable")))
                {
                    return Path.Combine(dir, FileName);
                }
            }

            return Path.Combine(appData, "ludusavi", FileName);
        }

        private static string ResolveExecutable(string executablePath, string pathVariable)
        {
            try
            {
                var name = string.IsNullOrWhiteSpace(executablePath) ? "ludusavi" : executablePath.Trim().Trim('"');
                if (Path.IsPathRooted(name))
                {
                    return File.Exists(name) ? name : null;
                }

                var withExe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
                return (pathVariable ?? string.Empty)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(dir => Path.Combine(dir.Trim().Trim('"'), withExe))
                    .FirstOrDefault(File.Exists);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static LudusaviRegistration Register(string configPath, string manifestPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    return LudusaviRegistration.NoConfig;
                }

                var original = File.ReadAllText(configPath, Encoding.UTF8);
                var updated = AddSecondaryManifest(original, manifestPath, out var result);
                if (result == LudusaviRegistration.Added)
                {
                    File.Copy(configPath, configPath + ".bak", true);
                    File.WriteAllText(configPath, updated, new UTF8Encoding(false));
                }

                return result;
            }
            catch (Exception)
            {
                return LudusaviRegistration.Failed;
            }
        }

        /// <summary>The config text with <paramref name="manifestPath"/> among the secondary manifests.</summary>
        public static string AddSecondaryManifest(string config, string manifestPath, out LudusaviRegistration result)
        {
            var newline = config.Contains("\r\n") ? "\r\n" : "\n";
            var lines = config.Replace("\r\n", "\n").Split('\n').ToList();
            var item = "    - path: " + Quote(manifestPath);

            var manifest = lines.FindIndex(l => Regex.IsMatch(l, @"^manifest:\s*$"));
            if (manifest < 0)
            {
                if (lines.Any(l => l.StartsWith("manifest:", StringComparison.Ordinal)))
                {
                    result = LudusaviRegistration.Unrecognized;
                    return config;
                }

                // Ludusavi fills in the rest of the section with its defaults.
                while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                {
                    lines.RemoveAt(lines.Count - 1);
                }

                lines.AddRange(new[] { "manifest:", "  secondary:", item, string.Empty });
                result = LudusaviRegistration.Added;
                return string.Join(newline, lines);
            }

            // The section: the indented lines after "manifest:".
            var end = manifest + 1;
            while (end < lines.Count && (lines[end].StartsWith(" ", StringComparison.Ordinal) || lines[end].Length == 0))
            {
                end++;
            }

            while (end > manifest + 1 && lines[end - 1].Length == 0)
            {
                end--;
            }

            var secondary = -1;
            for (var i = manifest + 1; i < end; i++)
            {
                if (Regex.IsMatch(lines[i], @"^  secondary:"))
                {
                    secondary = i;
                    break;
                }
            }

            if (secondary < 0)
            {
                lines.InsertRange(end, new[] { "  secondary:", item });
                result = LudusaviRegistration.Added;
                return string.Join(newline, lines);
            }

            var value = lines[secondary].Substring("  secondary:".Length).Trim();
            if (value == "[]")
            {
                lines[secondary] = "  secondary:";
            }
            else if (value.Length > 0)
            {
                result = LudusaviRegistration.Unrecognized;
                return config;
            }

            // Its items: the lines indented deeper than "  secondary:".
            var last = secondary + 1;
            while (last < end && (lines[last].StartsWith("   ", StringComparison.Ordinal) || lines[last].StartsWith("  - ", StringComparison.Ordinal)))
            {
                if (IsPathLine(lines[last], manifestPath))
                {
                    result = LudusaviRegistration.AlreadyRegistered;
                    return config;
                }

                last++;
            }

            var indent = secondary + 1 < last && lines[secondary + 1].StartsWith("  - ", StringComparison.Ordinal) ? "  " : "    ";
            lines.Insert(last, indent + "- path: " + Quote(manifestPath));
            result = LudusaviRegistration.Added;
            return string.Join(newline, lines);
        }

        private static bool IsPathLine(string line, string manifestPath)
        {
            var match = Regex.Match(line, @"^\s*(?:-\s+)?path:\s*(.+?)\s*$");
            return match.Success && string.Equals(Unquote(match.Groups[1].Value), manifestPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A YAML double-quoted scalar, as Ludusavi writes paths.</summary>
        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
            {
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            }

            return value;
        }
    }
}
