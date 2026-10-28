using System;
using System.IO;
using System.Linq;
using QuiverLibrary.Core.Executables;

namespace QuiverLibrary.Core
{
    public enum PortableOutcome
    {
        /// <summary>The marker file was created.</summary>
        Created,

        /// <summary>The marker file was already there (many releases ship it).</summary>
        AlreadyPortable,

        /// <summary>The app keeps its data in its own folder anyway.</summary>
        Builtin,

        /// <summary>The app has no portable mode.</summary>
        Unsupported,

        /// <summary>Not created: the app already has data in its usual folder, which it would stop seeing.</summary>
        HasUserData,

        /// <summary>The app's play program is unknown, so there's nowhere to put the marker.</summary>
        NoProgram,

        Failed,
    }

    public class PortableResult
    {
        public PortableOutcome Outcome { get; set; }

        /// <summary>The marker file (created or expected); null when the app needs none.</summary>
        public string MarkerPath { get; set; }

        /// <summary>
        /// The app's usual data folder when it holds something: why the marker wasn't created
        /// (<see cref="PortableOutcome.HasUserData"/>), or data the app no longer sees
        /// (<see cref="PortableOutcome.AlreadyPortable"/>). Null otherwise.
        /// </summary>
        public string UserDataPath { get; set; }

        public string Error { get; set; }
    }

    /// <summary>
    /// Makes an installed app portable: most recompilations and ports keep their saves and settings
    /// next to their program instead of in the user's folders when a marker file (usually an empty
    /// <c>portable.txt</c>) sits next to it. <see cref="UserDataInfo"/> says otherwise for the apps
    /// that differ; apps without it get <c>portable.txt</c>.
    /// </summary>
    public static class QuiverPortable
    {
        public static PortableResult Apply(string playProgram, UserDataInfo userData)
        {
            if (userData != null && userData.IsBuiltin)
            {
                return new PortableResult { Outcome = PortableOutcome.Builtin };
            }

            if (userData != null && userData.IsUnsupported)
            {
                return new PortableResult { Outcome = PortableOutcome.Unsupported };
            }

            var folder = string.IsNullOrWhiteSpace(playProgram) ? null : Path.GetDirectoryName(playProgram);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return new PortableResult { Outcome = PortableOutcome.NoProgram };
            }

            var result = new PortableResult
            {
                MarkerPath = Path.Combine(folder, userData?.Marker ?? UserDataInfo.DefaultMarker),
                UserDataPath = UserDataFolderWithData(userData),
            };

            if (File.Exists(result.MarkerPath))
            {
                result.Outcome = PortableOutcome.AlreadyPortable;
                return result;
            }

            if (result.UserDataPath != null)
            {
                result.Outcome = PortableOutcome.HasUserData;
                return result;
            }

            try
            {
                File.WriteAllBytes(result.MarkerPath, new byte[0]);
                result.Outcome = PortableOutcome.Created;
            }
            catch (Exception e)
            {
                result.Outcome = PortableOutcome.Failed;
                result.Error = e.Message;
            }

            return result;
        }

        /// <summary><see cref="UserDataInfo.AppData"/> with its environment variables expanded, when it exists and isn't empty.</summary>
        public static string UserDataFolderWithData(UserDataInfo userData)
        {
            if (string.IsNullOrWhiteSpace(userData?.AppData))
            {
                return null;
            }

            try
            {
                var path = Environment.ExpandEnvironmentVariables(userData.AppData);
                return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any() ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
