using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QuiverLibrary.Core
{
    /// <summary>A Playnite feature built from one or more of Quiver's tags.</summary>
    public sealed class QuiverFeature
    {
        /// <summary>Stable id stored in the settings (never shown).</summary>
        public string Key { get; }

        /// <summary>Feature name written to Playnite.</summary>
        public string Name { get; }

        public IReadOnlyList<string> QuiverTags { get; }

        public QuiverFeature(string key, string name, params string[] quiverTags)
        {
            Key = key;
            Name = name;
            QuiverTags = quiverTags;
        }
    }

    /// <summary>
    /// Maps Quiver's free-form <c>tags</c> to Playnite data: what the port offers becomes
    /// Features, the kind of project becomes Tags. Every other tag (original console,
    /// franchise...) is ignored.
    /// </summary>
    public static class QuiverTagMapping
    {
        public static readonly IReadOnlyList<QuiverFeature> Features = new[]
        {
            new QuiverFeature("keyboard-mouse", "Keyboard and Mouse", "keyboard and mouse"),
            new QuiverFeature("controller", "Controller Support", "controller support"),
            new QuiverFeature("mods", "Mod Support", "mod support"),
            new QuiverFeature("widescreen", "Widescreen", "widescreen"),
            new QuiverFeature("ultrawide", "Ultrawide", "ultrawide"),
            new QuiverFeature("unlocked-framerate", "Unlocked Framerate", "unlocked framerate"),
            new QuiverFeature("higher-resolution", "Increased Internal Resolution", "increased internal resolution"),
            new QuiverFeature("online-multiplayer", "Online Multiplayer", "online multiplayer"),
            new QuiverFeature("online-coop", "Online Co-op", "online co-op"),
            new QuiverFeature("split-screen", "Split Screen", "split screen"),
            new QuiverFeature("split-screen-coop", "Split-Screen Co-op", "split-screen co-op"),
        };

        private static readonly KeyValuePair<string, string[]>[] ProjectTypes =
        {
            new KeyValuePair<string, string[]>("Recompilation", new[] { "recompilation", "recomp" }),
            new KeyValuePair<string, string[]>("Decompilation", new[] { "decompilation", "decomp" }),
            new KeyValuePair<string, string[]>("Recreation", new[] { "recreation" }),
            new KeyValuePair<string, string[]>("ReXGlue", new[] { "rexglue" }),
            new KeyValuePair<string, string[]>("PSXRecomp", new[] { "psxrecomp" }),
            new KeyValuePair<string, string[]>("GBARecomp", new[] { "gbarecomp" }),
            new KeyValuePair<string, string[]>("Harbour Masters", new[] { "harbour masters" }),
        };

        private static readonly Dictionary<string, string> ModProviderNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "thunderstore", "Thunderstore" },
            { "gamebanana", "GameBanana" },
            { "nexusmods", "Nexus Mods" },
        };

        /// <summary>Feature names for the app, skipping the features whose <see cref="QuiverFeature.Key"/> is disabled.</summary>
        public static List<string> FeaturesFor(QuiverApp app, ICollection<string> disabledKeys = null)
        {
            var tags = TagSet(app);
            return Features
                .Where(f => disabledKeys == null || !disabledKeys.Contains(f.Key))
                .Where(f => f.QuiverTags.Any(tags.Contains))
                .Select(f => f.Name)
                .ToList();
        }

        public static List<string> ProjectTypeTagsFor(QuiverApp app)
        {
            var tags = TagSet(app);
            return ProjectTypes.Where(p => p.Value.Any(tags.Contains)).Select(p => p.Key).ToList();
        }

        /// <summary>One link per mod source: "Mods (Thunderstore)" → its page.</summary>
        public static List<KeyValuePair<string, string>> ModLinksFor(QuiverApp app)
        {
            return (app.Mods?.Sources ?? new List<QuiverModSource>())
                .Where(s => s != null && Uri.TryCreate(s.SourceUrl, UriKind.Absolute, out var uri) &&
                            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                .Select(s => new KeyValuePair<string, string>($"Mods ({ProviderName(s.Provider)})", s.SourceUrl))
                .GroupBy(l => l.Value, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static string ProviderName(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return "Web";
            }

            return ModProviderNames.TryGetValue(provider.Trim(), out var name)
                ? name
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(provider.Trim().ToLowerInvariant());
        }

        private static HashSet<string> TagSet(QuiverApp app)
        {
            return new HashSet<string>(
                (app.Tags ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
