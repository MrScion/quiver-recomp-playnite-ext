using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace QuiverLibrary.Core.Executables
{
    /// <summary>
    /// The player's display choice: the common one in the extension's settings, or (later) one game's own.
    /// Plain POCO so the Playnite settings can store it as it is.
    /// </summary>
    public class DisplayPreferences
    {
        /// <summary>Starts the games in full screen. Off = the game's own setting is left alone.</summary>
        public bool Fullscreen { get; set; }

        /// <summary>Internal render height (1080, 1440, 2160); 0 = the game's own setting is left alone.</summary>
        public int ResolutionHeight { get; set; }

        public bool ChangesNothing => !Fullscreen && ResolutionHeight <= 0;

        public DisplayPreferences Clone()
        {
            return new DisplayPreferences { Fullscreen = Fullscreen, ResolutionHeight = ResolutionHeight };
        }
    }

    /// <summary>A settings file and the lines to write to it.</summary>
    public class DisplayConfigEdit
    {
        public string Path { get; set; }

        public List<KeyValuePair<string, string>> Lines { get; set; } = new List<KeyValuePair<string, string>>();
    }

    /// <summary>What <see cref="DisplayPlanner.Plan"/> decided for one start of a game.</summary>
    public class DisplayPlan
    {
        public string Arguments { get; set; }

        /// <summary>Null when no file has to be written.</summary>
        public DisplayConfigEdit Config { get; set; }

        /// <summary>The scale used, or null when the resolution is left alone.</summary>
        public double? Scale { get; set; }
    }

    /// <summary>
    /// Applies <see cref="DisplayPreferences"/> to a game through its <see cref="DisplayInfo"/>: options added to the play
    /// program's arguments and <c>key = value</c> lines replaced in its settings file.
    /// </summary>
    public static class DisplayPlanner
    {
        public const string ScaleVariable = "{Scale}";

        /// <summary>The heights offered in the settings, after "the game's own".</summary>
        public static readonly int[] Heights = { 1080, 1440, 2160 };

        /// <summary>
        /// The game's own choice when it has one (<paramref name="perGame"/>, by app folder name), else the common one.
        /// </summary>
        public static DisplayPreferences Choose(DisplayPreferences common, IDictionary<string, DisplayPreferences> perGame, string folderName)
        {
            if (perGame != null && !string.IsNullOrEmpty(folderName))
            {
                var own = perGame.FirstOrDefault(p => string.Equals(p.Key, folderName, StringComparison.OrdinalIgnoreCase)).Value;
                if (own != null)
                {
                    return own;
                }
            }

            return common ?? new DisplayPreferences();
        }

        /// <summary>The internal resolution scale for <paramref name="height"/>, or null when it can't be set.</summary>
        public static double? Scale(DisplayInfo info, int height)
        {
            if (info == null || height <= 0 || info.NativeHeight <= 0)
            {
                return null;
            }

            var scale = (double)height / info.NativeHeight;
            scale = info.IntegerScale ? Math.Floor(scale + 0.5) : Math.Round(scale, 2, MidpointRounding.AwayFromZero);
            if (info.MaxScale > 0 && scale > info.MaxScale)
            {
                scale = info.MaxScale;
            }

            return Math.Max(info.IntegerScale ? 1 : 0.25, scale);
        }

        public static string FormatScale(double scale)
        {
            return scale.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <param name="appDir">The app folder.</param>
        /// <param name="program">The play program (absolute): its folder is where <see cref="DisplayInfo.ConfigFile"/> is.</param>
        public static DisplayPlan Plan(DisplayInfo info, DisplayPreferences prefs, string arguments, string appDir, string program)
        {
            var plan = new DisplayPlan { Arguments = arguments };
            if (info == null || prefs == null || prefs.ChangesNothing)
            {
                return plan;
            }

            plan.Scale = prefs.ResolutionHeight > 0 && info.SupportsResolution ? Scale(info, prefs.ResolutionHeight) : null;
            var scaleText = plan.Scale.HasValue ? FormatScale(plan.Scale.Value) : null;

            var lines = new List<KeyValuePair<string, string>>();
            if (prefs.Fullscreen)
            {
                plan.Arguments = MergeArguments(plan.Arguments, info.FullscreenArguments);
                lines.AddRange(ParseLines(info.FullscreenConfig));
            }

            if (scaleText != null)
            {
                plan.Arguments = MergeArguments(plan.Arguments, ExecutablePlanner.ReplaceVariable(info.ScaleArguments, ScaleVariable, scaleText));
                lines.AddRange(ParseLines(ExecutablePlanner.ReplaceVariable(info.ScaleConfig, ScaleVariable, scaleText)));
            }

            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(info.ConfigFile))
            {
                var file = ExecutablePlanner.ExpandVariables(info.ConfigFile, appDir, program);
                if (!System.IO.Path.IsPathRooted(file))
                {
                    var exeDir = program == null ? appDir : System.IO.Path.GetDirectoryName(program);
                    file = System.IO.Path.Combine(exeDir ?? string.Empty, file);
                }

                plan.Config = new DisplayConfigEdit { Path = file, Lines = lines };
            }

            return plan;
        }

        /// <summary>
        /// Adds <paramref name="extra"/> to <paramref name="arguments"/>; an option of it (<c>--name=value</c>) already in
        /// the arguments is replaced there instead of added twice.
        /// </summary>
        public static string MergeArguments(string arguments, string extra)
        {
            if (string.IsNullOrWhiteSpace(extra))
            {
                return arguments;
            }

            var result = arguments ?? string.Empty;
            var added = new List<string>();
            foreach (Match option in Regex.Matches(extra, "\"[^\"]*\"|\\S+"))
            {
                var token = option.Value;
                var name = Regex.Match(token, "^(--?[A-Za-z0-9_.-]+)=");
                if (name.Success)
                {
                    var existing = new Regex("(?<=^|\\s)" + Regex.Escape(name.Groups[1].Value) + "=(\"[^\"]*\"|\\S*)", RegexOptions.IgnoreCase);
                    if (existing.IsMatch(result))
                    {
                        result = existing.Replace(result, m => token, 1);
                        continue;
                    }
                }

                added.Add(token);
            }

            if (added.Count == 0)
            {
                return result;
            }

            return string.IsNullOrWhiteSpace(result) ? string.Join(" ", added) : result.TrimEnd() + " " + string.Join(" ", added);
        }

        /// <summary><c>key = value; key2 = value2</c> as pairs.</summary>
        public static List<KeyValuePair<string, string>> ParseLines(string text)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return list;
            }

            foreach (var part in text.Split(';'))
            {
                var at = part.IndexOf('=');
                if (at > 0)
                {
                    list.Add(new KeyValuePair<string, string>(part.Substring(0, at).Trim(), part.Substring(at + 1).Trim()));
                }
            }

            return list;
        }

        /// <summary>
        /// Replaces the value of each existing <c>key = value</c> line (not commented out, any section) and keeps the rest of
        /// the line's layout. Keys not found are returned in <paramref name="missing"/> and not added.
        /// </summary>
        public static string EditConfigText(string text, IEnumerable<KeyValuePair<string, string>> lines, out List<string> missing)
        {
            missing = new List<string>();
            foreach (var line in lines)
            {
                var pattern = new Regex("^([ \\t]*" + Regex.Escape(line.Key) + "[ \\t]*=[ \\t]*)([^\\r\\n]*?)([ \\t]*)(?=\\r?$)",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (!pattern.IsMatch(text))
                {
                    missing.Add(line.Key);
                    continue;
                }

                var value = line.Value;
                text = pattern.Replace(text, m => m.Groups[1].Value + value + m.Groups[3].Value);
            }

            return text;
        }

        /// <summary>
        /// Writes <paramref name="edit"/> to its file when something changes (same encoding, no BOM added). Returns the keys
        /// not found; throws when the file can't be read or written.
        /// </summary>
        public static List<string> Apply(DisplayConfigEdit edit, out bool changed)
        {
            changed = false;
            if (edit == null)
            {
                return new List<string>();
            }

            if (!File.Exists(edit.Path))
            {
                return edit.Lines.Select(l => l.Key).ToList();
            }

            var bytes = File.ReadAllBytes(edit.Path);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
            var edited = EditConfigText(text, edit.Lines, out var missing);
            if (edited != text)
            {
                File.WriteAllBytes(edit.Path, new UTF8Encoding(hasBom).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(edited)).ToArray());
                changed = true;
            }

            return missing;
        }
    }
}
