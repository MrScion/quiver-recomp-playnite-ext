using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;
using QuiverLibrary.Core.Executables;

namespace QuiverLibrary.Playnite.Setup
{
    /// <summary>
    /// Sidebar panel (Desktop mode): a table of the installed apps whose game is installed or built by their own
    /// installer or launcher (type, version set up, version downloaded, state, actions), with an extra line under a row
    /// only when there is something to say (running, failed, to do by hand) or its details are open. Runs their update
    /// commands through <see cref="SetupRunner"/>. Built in code: no XAML compiler without Visual Studio.
    /// </summary>
    public class SetupPanelView : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private const int ColumnCount = 6;

        private readonly QuiverLibraryPlugin plugin;
        private readonly Grid table = new Grid();
        private readonly TextBlock status;
        private readonly Button refresh;
        private readonly Style textStyle;

        /// <summary>The parts of each row that follow its job, by app folder name.</summary>
        private readonly Dictionary<string, RowParts> parts = new Dictionary<string, RowParts>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Apps whose details are open, kept across reloads.</summary>
        private readonly HashSet<string> openDetails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private class RowParts
        {
            public SetupAppRow Row;
            public TextBlock State;
            public WrapPanel Buttons;
            public StackPanel Extra;
            public TextBlock JobLine;
            public TextBlock Details;
            public Button Reinstall;
        }

        public SetupPanelView(QuiverLibraryPlugin plugin)
        {
            this.plugin = plugin;
            // A Style made in code loses the theme's text colour (black on dark themes): base it on the theme's
            // BaseTextBlockStyle and take its TextBrush explicitly.
            var baseStyle = Application.Current.TryFindResource("BaseTextBlockStyle") as Style
                ?? Application.Current.TryFindResource(typeof(TextBlock)) as Style;
            textStyle = new Style(typeof(TextBlock), baseStyle);
            textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrush")));

            var root = new DockPanel { Margin = new Thickness(20), LastChildFill = true };

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            refresh = new Button { Content = Loc("LOCQuiverSetupPanelRefresh"), Padding = new Thickness(12, 4, 12, 4) };
            refresh.Click += (s, e) => Reload();
            DockPanel.SetDock(refresh, Dock.Right);
            header.Children.Add(refresh);
            var title = Text(Loc("LOCQuiverSetupPanelTitle"));
            title.FontSize = 20;
            title.FontWeight = FontWeights.Bold;
            header.Children.Add(title);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var hint = Text(Loc("LOCQuiverSetupPanelHint"));
            hint.Opacity = 0.75;
            hint.Margin = new Thickness(0, 0, 0, 6);
            DockPanel.SetDock(hint, Dock.Top);
            root.Children.Add(hint);

            status = Text(string.Empty);
            status.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(status, Dock.Top);
            root.Children.Add(status);

            // Name and state take the room left; the others fit their content.
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 160 });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star), MinWidth = 120 });
            // Actions get a share too, so their buttons wrap instead of widening the table.
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star), MinWidth = 220 });
            root.Children.Add(new ScrollViewer
            {
                Content = table,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            Content = root;

            Action changed = () => Dispatcher.BeginInvoke((Action)UpdateJobs);
            Action<SetupJob> finished = job => Dispatcher.BeginInvoke((Action)Reload);
            Loaded += (s, e) =>
            {
                plugin.Runner.Changed += changed;
                plugin.Runner.Finished += finished;
            };
            Unloaded += (s, e) =>
            {
                plugin.Runner.Changed -= changed;
                plugin.Runner.Finished -= finished;
            };
            Reload();
        }

        private static string Loc(string key) => ResourceProvider.GetString(key);

        /// <summary>Reads the apps again; Quiver's folder may be on a network drive, so not on the UI thread.</summary>
        private void Reload()
        {
            refresh.IsEnabled = false;
            status.Text = Loc("LOCQuiverSetupPanelLoading");
            Task.Run(() => plugin.LoadSetupRows()).ContinueWith(t => Dispatcher.Invoke(() =>
            {
                refresh.IsEnabled = true;
                table.Children.Clear();
                table.RowDefinitions.Clear();
                parts.Clear();
                if (t.IsFaulted)
                {
                    Logger.Error(t.Exception, "Cannot read the Quiver apps set up by hand.");
                    status.Text = string.Format(Loc("LOCQuiverSetupPanelError"), t.Exception?.GetBaseException().Message);
                    return;
                }

                var rows = t.Result;
                status.Text = rows.Count == 0
                    ? Loc("LOCQuiverSetupPanelEmpty")
                    : string.Format(Loc("LOCQuiverSetupPanelSummary"), rows.Count, rows.Count(r => r.IsPending));
                if (rows.Count == 0)
                {
                    return;
                }

                AddHeader();
                foreach (var row in rows.OrderByDescending(r => r.IsPending).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    AddRow(row);
                }

                UpdateJobs();
            }));
        }

        private void AddHeader()
        {
            var index = NewRow();
            var keys = new[] { "LOCQuiverSetupColGame", "LOCQuiverSetupColKind", "LOCQuiverSetupColInstalled", "LOCQuiverSetupColDownloaded", "LOCQuiverSetupColState", "LOCQuiverSetupColActions" };
            for (var column = 0; column < keys.Length; column++)
            {
                var cell = Text(Loc(keys[column]));
                cell.FontWeight = FontWeights.Bold;
                cell.Opacity = 0.8;
                Place(Cell(cell), index, column);
            }

            Place(Line(2), index, 0, ColumnCount);
        }

        private void AddRow(SetupAppRow row)
        {
            var index = NewRow();

            var name = Text(row.Name);
            name.FontWeight = FontWeights.SemiBold;
            Place(Cell(name), index, 0);
            Place(Cell(Text(Loc(row.Setup.IsInstaller ? "LOCQuiverSetupKindInstallerShort" : "LOCQuiverSetupKindBuilderShort"))), index, 1);
            Place(Cell(Text(row.InstalledVersion ?? "—")), index, 2);
            var downloaded = Text(row.DownloadedVersion ?? "—");
            if (row.IsPending)
            {
                downloaded.FontWeight = FontWeights.SemiBold;
            }

            Place(Cell(downloaded), index, 3);
            var state = Text(string.Empty);
            Place(Cell(state), index, 4);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            Place(Cell(buttons), index, 5);

            // The extra line under the row: what is running or failed, what to do by hand, the details when open.
            var extraIndex = NewRow();
            var extra = new StackPanel { Margin = new Thickness(12, 0, 6, 8) };
            var jobLine = Text(string.Empty);
            jobLine.Opacity = 0.85;
            extra.Children.Add(jobLine);
            if (row.IsPending && row.Update != null && !row.Update.CanRun && !row.Update.CanAsk)
            {
                var manual = Text(string.Format(Loc("LOCQuiverSetupManual"), UpdateProblem(row.Update)));
                manual.Opacity = 0.85;
                manual.Margin = new Thickness(0, 2, 0, 0);
                extra.Children.Add(manual);
            }

            var details = Text(Details(row) ?? string.Empty);
            details.Opacity = 0.8;
            details.FontSize = Math.Max(10, details.FontSize - 1);
            details.Margin = new Thickness(0, 4, 0, 0);
            extra.Children.Add(details);
            // Running the update again while up to date (to repair an install) is only offered with the details open.
            var reinstall = ActionButton(Loc("LOCQuiverSetupReinstall"), () =>
            {
                plugin.RequestUpdate(row);
                UpdateJobs();
            });
            reinstall.HorizontalAlignment = HorizontalAlignment.Left;
            reinstall.Margin = new Thickness(0, 6, 0, 0);
            extra.Children.Add(reinstall);
            Place(extra, extraIndex, 0, ColumnCount);
            Place(Line(1), extraIndex, 0, ColumnCount);

            parts[row.FolderName] = new RowParts { Row = row, State = state, Buttons = buttons, Extra = extra, JobLine = jobLine, Details = details, Reinstall = reinstall };
        }

        /// <summary>Shows each row's state (with its job), the buttons that fit it and its extra line.</summary>
        private void UpdateJobs()
        {
            foreach (var part in parts.Values)
            {
                var row = part.Row;
                var job = plugin.Runner.Find(row.FolderName);
                var active = job != null && job.IsActive;

                part.State.Text = StateText(row, job);
                part.State.FontWeight = row.IsPending || active ? FontWeights.SemiBold : FontWeights.Normal;
                if (job?.State == SetupJobState.Failed)
                {
                    part.State.Foreground = Brushes.OrangeRed;
                }
                else if (active)
                {
                    part.State.Foreground = Brushes.DeepSkyBlue;
                }
                else if (row.IsPending)
                {
                    part.State.Foreground = Brushes.Orange;
                }
                else
                {
                    part.State.ClearValue(TextBlock.ForegroundProperty);
                }

                part.Buttons.Children.Clear();
                if (active)
                {
                    part.Buttons.Children.Add(ActionButton(Loc("LOCQuiverSetupCancel"), () => plugin.Runner.Cancel(row.FolderName)));
                }
                else
                {
                    if (row.IsPending && CanUpdate(row))
                    {
                        part.Buttons.Children.Add(ActionButton(Loc("LOCQuiverSetupUpdate"), () =>
                        {
                            plugin.RequestUpdate(row);
                            UpdateJobs();
                        }));
                    }

                    part.Buttons.Children.Add(ActionButton(Loc(row.Setup.IsInstaller ? "LOCQuiverSetupOpenInstaller" : "LOCQuiverSetupOpenLauncher"), () => plugin.RunSetup(row)));
                    if (row.IsPending)
                    {
                        part.Buttons.Children.Add(ActionButton(Loc("LOCQuiverSetupMarkDone"), () =>
                        {
                            plugin.MarkSetupDone(row);
                            Reload();
                        }));
                    }
                }

                part.Buttons.Children.Add(ActionButton(Loc("LOCQuiverSetupOpenFolder"), () => plugin.OpenAppFolder(row)));
                if (job != null && System.IO.File.Exists(job.LogPath))
                {
                    var logPath = job.LogPath;
                    part.Buttons.Children.Add(ActionButton(Loc("LOCQuiverSetupViewLog"), () => QuiverLibraryPlugin.OpenFile(logPath)));
                }

                if (!active && row.Files.Count > 0)
                {
                    part.Buttons.Children.Add(ActionButton(Loc("LOCQuiverSetupForgetFiles"), () =>
                    {
                        plugin.ForgetSetupFiles(row);
                        Reload();
                    }));
                }

                var detailsOpen = openDetails.Contains(row.FolderName);
                if (part.Details.Text.Length > 0)
                {
                    var folder = row.FolderName;
                    part.Buttons.Children.Add(ActionButton(Loc(detailsOpen ? "LOCQuiverSetupHideDetails" : "LOCQuiverSetupDetails"), () =>
                    {
                        if (!openDetails.Remove(folder))
                        {
                            openDetails.Add(folder);
                        }

                        UpdateJobs();
                    }));
                }

                part.JobLine.Text = JobLine(job);
                part.JobLine.Visibility = part.JobLine.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                part.Details.Visibility = detailsOpen && part.Details.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                part.Reinstall.Visibility = detailsOpen && !active && !row.IsPending && CanUpdate(row) ? Visibility.Visible : Visibility.Collapsed;
                part.Extra.Visibility = part.Extra.Children.OfType<UIElement>().Any(c => c.Visibility == Visibility.Visible)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private static bool CanUpdate(SetupAppRow row) => row.Update != null && (row.Update.CanRun || row.Update.CanAsk);

        private static string StateText(SetupAppRow row, SetupJob job)
        {
            switch (job?.State)
            {
                case SetupJobState.Queued:
                    return Loc("LOCQuiverSetupStateQueued");
                case SetupJobState.Running:
                    var minutes = job.Started == null ? 0 : (int)(DateTime.Now - job.Started.Value).TotalMinutes;
                    return string.Format(Loc("LOCQuiverSetupStateRunning"), minutes);
                case SetupJobState.Failed:
                    return Loc("LOCQuiverSetupStateFailed");
            }

            return Loc(row.IsPending ? "LOCQuiverSetupStatePendingShort" : "LOCQuiverSetupStateUpToDateShort");
        }

        /// <summary>Under the row: the running job's last line, or how the last one ended.</summary>
        private static string JobLine(SetupJob job)
        {
            if (job == null)
            {
                return string.Empty;
            }

            switch (job.State)
            {
                case SetupJobState.Queued:
                    return Loc("LOCQuiverSetupJobQueued");
                case SetupJobState.Running:
                    return job.LastLine ?? string.Empty;
                case SetupJobState.Succeeded:
                    return string.Format(Loc("LOCQuiverSetupJobSucceeded"), job.Ended);
                case SetupJobState.Canceled:
                    return Loc("LOCQuiverSetupJobCanceled");
                default:
                    return string.Format(Loc("LOCQuiverSetupJobFailed"), job.ExitCode?.ToString() ?? "?") + (string.IsNullOrEmpty(job.LastLine) ? string.Empty : "\n" + job.LastLine);
            }
        }

        private static string UpdateProblem(PreparedCommand command)
        {
            if (command.Problem != null)
            {
                return command.Problem;
            }

            return string.Format(Loc("LOCQuiverSetupMissing"), string.Join(", ", command.Missing));
        }

        /// <summary>What <c>executables.json</c> knows of its setup: the commands, the files chosen and the notes.</summary>
        private static string Details(SetupAppRow row)
        {
            var text = new StringBuilder();
            text.AppendLine(Loc(row.Setup.IsInstaller ? "LOCQuiverSetupKindInstaller" : "LOCQuiverSetupKindBuilder"));
            if (row.Program != null)
            {
                text.AppendLine(string.Format(Loc("LOCQuiverSetupProgram"), row.Program.Path));
            }

            if (row.Update != null && row.Update.File != null)
            {
                text.AppendLine(Loc("LOCQuiverSetupCommandUpdate")).AppendLine("    " + row.Update.Display);
            }
            else
            {
                AppendCommand(text, "LOCQuiverSetupCommandUpdate", row.Setup.Update);
            }

            AppendCommand(text, "LOCQuiverSetupCommandInstall", row.Setup.Install);
            foreach (var file in row.Files)
            {
                var exists = System.IO.File.Exists(file.Value) || System.IO.Directory.Exists(file.Value);
                text.AppendLine(string.Format(Loc(exists ? "LOCQuiverSetupRememberedFile" : "LOCQuiverSetupRememberedFileGone"), file.Key, file.Value));
            }

            if (row.Setup.Update == null && row.Setup.Install == null)
            {
                text.AppendLine(Loc("LOCQuiverSetupNoCommand"));
            }

            if (!string.IsNullOrWhiteSpace(row.Setup.Notes))
            {
                text.AppendLine().AppendLine(row.Setup.Notes);
            }

            return text.ToString().TrimEnd();
        }

        private static void AppendCommand(StringBuilder text, string key, ExecutableTarget command)
        {
            if (command != null)
            {
                text.AppendLine(Loc(key)).AppendLine("    " + (command.File + " " + (command.Arguments ?? string.Empty)).Trim());
            }
        }

        private int NewRow()
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            return table.RowDefinitions.Count - 1;
        }

        private void Place(UIElement element, int row, int column, int span = 1)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
            table.Children.Add(element);
        }

        private static FrameworkElement Cell(FrameworkElement content)
        {
            content.VerticalAlignment = VerticalAlignment.Center;
            content.Margin = new Thickness(6, 6, 12, 6);
            return content;
        }

        /// <summary>A rule along the bottom of a row.</summary>
        private static Border Line(double thickness)
        {
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, thickness), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
            line.SetResourceReference(Border.BorderBrushProperty, "NormalBorderBrush");
            return line;
        }

        private Button ActionButton(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(4, 2, 0, 2), Padding = new Thickness(8, 2, 8, 2) };
            button.Click += (s, e) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Setup panel action '{text}' failed.");
                }
            };
            return button;
        }

        private TextBlock Text(string text) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Style = textStyle };
    }
}
