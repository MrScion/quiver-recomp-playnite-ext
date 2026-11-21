using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Playnite.SDK;

namespace QuiverLibrary.Playnite.Settings
{
    /// <summary>
    /// Settings view built in code: <c>dotnet build</c> without Visual Studio
    /// installed can't compile XAML markup for net462 (no PresentationBuildTasks).
    /// One tab per group: general, saves and achievements, display, import.
    /// </summary>
    public class QuiverSettingsView : UserControl
    {
        public QuiverSettingsView()
        {
            var tabs = new TabControl { Margin = new Thickness(10) };
            tabs.Items.Add(Tab("LOCQuiverSettingsTabGeneral", BuildGeneral()));
            tabs.Items.Add(Tab("LOCQuiverSettingsTabSaves", BuildSaves()));
            tabs.Items.Add(Tab("LOCQuiverSettingsTabDisplay", BuildDisplay()));
            tabs.Items.Add(Tab("LOCQuiverSettingsTabImport", BuildImport()));
            Content = tabs;
        }

        private static TabItem Tab(string headerKey, UIElement content)
        {
            return new TabItem
            {
                Header = ResourceProvider.GetString(headerKey),
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = content,
                },
            };
        }

        /// <summary>Quiver's folder, what was found there, the last played time and how the library works.</summary>
        private static StackPanel BuildGeneral()
        {
            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(Header("LOCQuiverSettingsFolderHeader", 0));
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsFolderHint")));

            var folderRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };
            var browse = new Button
            {
                Content = ResourceProvider.GetString("LOCQuiverSettingsBrowse"),
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2),
            };
            browse.SetBinding(Button.CommandProperty, new Binding(nameof(QuiverSettingsViewModel.BrowseQuiverFolderCommand)));
            DockPanel.SetDock(browse, Dock.Right);
            folderRow.Children.Add(browse);

            var folderBox = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
            folderBox.SetBinding(TextBox.TextProperty, new Binding(nameof(QuiverSettingsViewModel.QuiverFolder))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            folderRow.Children.Add(folderBox);
            root.Children.Add(folderRow);

            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            status.SetBinding(TextBlock.TextProperty, new Binding(nameof(QuiverSettingsViewModel.Status)));
            // A Style set in code drops the theme's foreground and falls back to black,
            // unreadable on dark themes. The Default theme has no implicit TextBlock style,
            // so base it on BaseTextBlockStyle and take the theme's TextBrush explicitly.
            var baseStyle = Application.Current.TryFindResource("BaseTextBlockStyle") as Style
                ?? Application.Current.TryFindResource(typeof(TextBlock)) as Style;
            var errorStyle = new Style(typeof(TextBlock), baseStyle);
            errorStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
            var errorTrigger = new DataTrigger { Binding = new Binding(nameof(QuiverSettingsViewModel.StatusIsError)), Value = true };
            errorTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.OrangeRed));
            errorStyle.Triggers.Add(errorTrigger);
            status.Style = errorStyle;
            root.Children.Add(status);

            root.Children.Add(Header("LOCQuiverSettingsGamesFolderHeader", 20));
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsGamesFolderHint")));
            var gamesRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };
            var browseGames = new Button
            {
                Content = ResourceProvider.GetString("LOCQuiverSettingsBrowse"),
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2),
            };
            browseGames.SetBinding(Button.CommandProperty, new Binding(nameof(QuiverSettingsViewModel.BrowseGamesFolderCommand)));
            DockPanel.SetDock(browseGames, Dock.Right);
            gamesRow.Children.Add(browseGames);
            var gamesBox = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
            gamesBox.SetBinding(TextBox.TextProperty, new Binding(nameof(QuiverSettingsViewModel.GamesFolder))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            gamesRow.Children.Add(gamesBox);
            root.Children.Add(gamesRow);

            AddCheck(root, "LOCQuiverSettingsSyncLastPlayed", "LOCQuiverSettingsSyncLastPlayedHint", nameof(QuiverSettingsViewModel.SyncLastPlayed));
            AddCheck(root, "LOCQuiverSettingsGamepadExit", "LOCQuiverSettingsGamepadExitHint", nameof(QuiverSettingsViewModel.GamepadExit));

            root.Children.Add(Header("LOCQuiverSettingsHowItWorksHeader", 20));
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsHowItWorks")));
            return root;
        }

        /// <summary>Portable installs, Ludusavi backups and achievements.</summary>
        private static StackPanel BuildSaves()
        {
            var root = new StackPanel { Margin = new Thickness(16) };
            AddCheck(root, "LOCQuiverSettingsMakePortable", "LOCQuiverSettingsMakePortableHint", nameof(QuiverSettingsViewModel.MakePortable), 0);
            AddCheck(root, "LOCQuiverSettingsLudusaviBackup", "LOCQuiverSettingsLudusaviBackupHint", nameof(QuiverSettingsViewModel.LudusaviBackup));
            AddCheck(root, "LOCQuiverSettingsAchievementsExport", "LOCQuiverSettingsAchievementsExportHint", nameof(QuiverSettingsViewModel.AchievementsExport));
            return root;
        }

        /// <summary>Common display settings: full screen and internal resolution.</summary>
        private static StackPanel BuildDisplay()
        {
            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(Header("LOCQuiverSettingsDisplayHeader", 0));
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsDisplayHint")));

            AddCheck(root, "LOCQuiverSettingsDisplayFullscreen", "LOCQuiverSettingsDisplayFullscreenHint", nameof(QuiverSettingsViewModel.DisplayFullscreen));

            root.Children.Add(new TextBlock
            {
                Text = ResourceProvider.GetString("LOCQuiverSettingsDisplayResolution"),
                Margin = new Thickness(0, 16, 0, 4),
            });
            var resolution = new ComboBox
            {
                Width = 260,
                HorizontalAlignment = HorizontalAlignment.Left,
                DisplayMemberPath = nameof(QuiverResolutionOption.Name),
                SelectedValuePath = nameof(QuiverResolutionOption.Height),
                Margin = new Thickness(0, 0, 0, 4),
            };
            resolution.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(QuiverSettingsViewModel.Resolutions)));
            resolution.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(QuiverSettingsViewModel.DisplayHeight)) { Mode = BindingMode.TwoWay });
            root.Children.Add(resolution);
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsDisplayResolutionHint")));
            return root;
        }

        /// <summary>Port features imported from Quiver's tags.</summary>
        private static StackPanel BuildImport()
        {
            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(Header("LOCQuiverSettingsFeaturesHeader", 0));
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsFeaturesHint")));
            root.Children.Add(BuildFeatureList());
            return root;
        }

        /// <summary>One checkbox per <see cref="QuiverSettingsViewModel.Features"/> entry, wrapping into columns.</summary>
        private static ItemsControl BuildFeatureList()
        {
            var check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(ContentControl.ContentProperty, new Binding(nameof(QuiverFeatureOption.Name)));
            check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(QuiverFeatureOption.IsEnabled)) { Mode = BindingMode.TwoWay });
            check.SetValue(FrameworkElement.WidthProperty, 260.0);
            check.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 12, 4));

            var list = new ItemsControl
            {
                Margin = new Thickness(0, 8, 0, 0),
                ItemTemplate = new DataTemplate { VisualTree = check },
                ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel))),
            };
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(QuiverSettingsViewModel.Features)));
            return list;
        }

        private static void AddCheck(Panel root, string textKey, string hintKey, string property, double top = 16)
        {
            var check = new CheckBox
            {
                Content = ResourceProvider.GetString(textKey),
                Margin = new Thickness(0, top, 0, 4),
            };
            check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            root.Children.Add(check);
            root.Children.Add(Hint(ResourceProvider.GetString(hintKey)));
        }

        private static TextBlock Header(string key, double top)
        {
            return new TextBlock
            {
                Text = ResourceProvider.GetString(key),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, top, 0, 4),
            };
        }

        private static TextBlock Hint(string text)
        {
            return new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
            };
        }
    }
}
