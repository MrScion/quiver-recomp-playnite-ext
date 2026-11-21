using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Playnite.SDK;
using QuiverLibrary.Core.Gamepad;

namespace QuiverLibrary.Playnite.Controllers
{
    /// <summary>
    /// While a recomp runs, watches the XInput gamepads for Back held + Start and then closes the game:
    /// first like its window's close button, and after <see cref="GracePeriod"/> by ending its programs.
    /// XInput is read in the background, so it works while the game has the focus. Controllers that
    /// aren't XInput (a DualSense without Steam Input, DS4Windows or reWASD) aren't seen.
    /// </summary>
    public sealed class GamepadExitWatcher : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);
        private const int PollMilliseconds = 50;
        private const int DisconnectedRetryMilliseconds = 2000;

        private readonly string _gameName;
        private readonly int _startedProcessId;
        private readonly string _startedProgram;
        private readonly List<string> _folders;
        private readonly Thread _thread;
        private volatile bool _stop;

        /// <param name="startedProcessId">What Playnite started (0 if unknown).</param>
        /// <param name="folders">The game's folders: its app folder and the folder of its Play program.</param>
        public GamepadExitWatcher(string gameName, int startedProcessId, IEnumerable<string> folders)
        {
            _gameName = gameName;
            _startedProcessId = startedProcessId;
            _startedProgram = startedProcessId > 0 ? ProcessTools.ProgramOf(startedProcessId) : null;
            var all = new List<string>(folders ?? new string[0]);
            if (!string.IsNullOrEmpty(_startedProgram))
            {
                all.Add(Path.GetDirectoryName(_startedProgram));
            }

            _folders = GameProcessFilter.Folders(all);
            _thread = new Thread(Run) { IsBackground = true, Name = "Quiver gamepad exit" };
        }

        public void Start()
        {
            Logger.Info($"Gamepad exit (Back + Start) watching {_gameName}: process {_startedProcessId} ({_startedProgram ?? "unknown"}), folders {string.Join(" · ", _folders)}.");
            _thread.Start();
        }

        public void Dispose()
        {
            _stop = true;
        }

        private void Run()
        {
            if (!XInput.Available)
            {
                Logger.Warn("Gamepad exit: XInput isn't available (no xinput1_4.dll or xinput9_1_0.dll).");
                return;
            }

            var combo = new ExitCombo();
            var nextTry = new DateTime[ExitCombo.MaxPads];
            while (!_stop)
            {
                var now = DateTime.UtcNow;
                for (var pad = 0; pad < ExitCombo.MaxPads && !_stop; pad++)
                {
                    // Asking a disconnected slot is slow: only every few seconds.
                    if (now < nextTry[pad])
                    {
                        continue;
                    }

                    if (!XInput.TryGetButtons(pad, out var buttons))
                    {
                        combo.Reset(pad);
                        nextTry[pad] = now.AddMilliseconds(DisconnectedRetryMilliseconds);
                        continue;
                    }

                    if (combo.Update(pad, buttons))
                    {
                        Logger.Info($"Gamepad exit: Back + Start on pad {pad + 1}, closing {_gameName}.");
                        try
                        {
                            CloseGame();
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, $"Gamepad exit: cannot close {_gameName}.");
                        }

                        // Once is enough; Playnite stops the watcher when the game stops.
                        _stop = true;
                        return;
                    }
                }

                Thread.Sleep(PollMilliseconds);
            }
        }

        /// <summary>The game's running programs: what Playnite started and its children, and anything run from the game's folders.</summary>
        private List<int> GameProcesses()
        {
            var own = Process.GetCurrentProcess().Id;
            var snapshot = ProcessTools.Snapshot();
            var ids = new HashSet<int>();

            // The started process counts only while it is still the same program (process ids are reused).
            if (_startedProcessId > 0 && snapshot.ContainsKey(_startedProcessId)
                && string.Equals(ProcessTools.ProgramOf(_startedProcessId), _startedProgram, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(_startedProcessId);
            }

            foreach (var id in snapshot.Keys)
            {
                if (id != own && id > 4 && GameProcessFilter.IsInside(ProcessTools.ProgramOf(id), _folders))
                {
                    ids.Add(id);
                }
            }

            // Children of those (a launcher's game, a .bat's program), wherever they run from. A child must
            // be newer than its parent: an old program whose dead parent's id was reused isn't one.
            var added = true;
            while (added)
            {
                added = false;
                foreach (var pair in snapshot)
                {
                    if (pair.Key != own && !ids.Contains(pair.Key) && ids.Contains(pair.Value)
                        && ProcessTools.StartedAfter(pair.Key, pair.Value))
                    {
                        ids.Add(pair.Key);
                        added = true;
                    }
                }
            }

            ids.Remove(own);
            return ids.ToList();
        }

        private void CloseGame()
        {
            var ids = GameProcesses();
            if (ids.Count == 0)
            {
                Logger.Warn($"Gamepad exit: no running program of {_gameName} found.");
                return;
            }

            Logger.Info($"Gamepad exit: programs of {_gameName}: {string.Join(", ", ids.Select(id => id + " " + Path.GetFileName(ProcessTools.ProgramOf(id) ?? "?")))}.");
            var windows = ProcessTools.CloseWindows(new HashSet<int>(ids));
            Logger.Info($"Gamepad exit: asked {windows} window(s) to close.");

            var deadline = DateTime.UtcNow + GracePeriod;
            while (DateTime.UtcNow < deadline && ids.Any(ProcessTools.IsRunning))
            {
                Thread.Sleep(200);
            }

            foreach (var id in ids.Where(ProcessTools.IsRunning))
            {
                try
                {
                    using (var process = Process.GetProcessById(id))
                    {
                        process.Kill();
                    }

                    Logger.Info($"Gamepad exit: ended process {id} (still running after {GracePeriod.TotalSeconds} s).");
                }
                catch (Exception e)
                {
                    Logger.Warn($"Gamepad exit: cannot end process {id}: {e.Message}");
                }
            }
        }
    }

    /// <summary>XInput, from xinput1_4.dll (Windows 8+) or xinput9_1_0.dll.</summary>
    internal static class XInput
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int GetState14(int userIndex, out XInputState state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int GetState910(int userIndex, out XInputState state);

        private static int _version; // 0 = unknown, 14, 910, -1 = none

        public static bool Available
        {
            get
            {
                if (_version == 0)
                {
                    _version = Probe();
                }

                return _version > 0;
            }
        }

        private static int Probe()
        {
            try
            {
                GetState14(0, out _);
                return 14;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
            }

            try
            {
                GetState910(0, out _);
                return 910;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
            }

            return -1;
        }

        /// <summary>False when no gamepad is connected in that slot.</summary>
        public static bool TryGetButtons(int pad, out ushort buttons)
        {
            buttons = 0;
            if (!Available)
            {
                return false;
            }

            XInputState state;
            var result = _version == 14 ? GetState14(pad, out state) : GetState910(pad, out state);
            if (result != 0)
            {
                return false;
            }

            buttons = state.Gamepad.Buttons;
            return true;
        }
    }

    /// <summary>Process list, program paths (also of 64-bit programs from 32-bit Playnite) and windows.</summary>
    internal static class ProcessTools
    {
        private const uint Th32csSnapProcess = 0x00000002;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint Synchronize = 0x00100000;
        private const uint WmClose = 0x0010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int PriorityClassBase;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExeFile;
        }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        /// <summary>Running processes: id → parent id.</summary>
        public static Dictionary<int, int> Snapshot()
        {
            var result = new Dictionary<int, int>();
            var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
            if (snapshot == InvalidHandle || snapshot == IntPtr.Zero)
            {
                return result;
            }

            try
            {
                var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry32)) };
                if (!Process32FirstW(snapshot, ref entry))
                {
                    return result;
                }

                do
                {
                    result[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                }
                while (Process32NextW(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return result;
        }

        /// <summary>Full path of a process's program, or null.</summary>
        public static string ProgramOf(int processId)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var size = 1024;
                var name = new StringBuilder(size);
                return QueryFullProcessImageNameW(handle, 0, name, ref size) ? name.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>Creation time (FILETIME), or null when it can't be read.</summary>
        public static long? CreationTime(int processId)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return GetProcessTimes(handle, out var created, out _, out _, out _) ? created : (long?)null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>True when <paramref name="child"/> was created after <paramref name="parent"/> (false if unknown).</summary>
        public static bool StartedAfter(int child, int parent)
        {
            var childTime = CreationTime(child);
            var parentTime = CreationTime(parent);
            return childTime.HasValue && parentTime.HasValue && childTime.Value >= parentTime.Value;
        }

        public static bool IsRunning(int processId)
        {
            var handle = OpenProcess(Synchronize, false, processId);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return WaitForSingleObject(handle, 0) != 0; // WAIT_OBJECT_0 = it has ended
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>Sends WM_CLOSE (the close button) to the visible top-level windows of those processes; returns how many.</summary>
        public static int CloseWindows(HashSet<int> processIds)
        {
            var count = 0;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var processId);
                if (processIds.Contains(processId) && IsWindowVisible(window) && PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero))
                {
                    count++;
                }

                return true;
            }, IntPtr.Zero);
            return count;
        }
    }
}
