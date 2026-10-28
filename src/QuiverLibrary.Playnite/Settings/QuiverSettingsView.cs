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
    /// </summary>
    public class QuiverSettingsView : UserControl
    {
        public QuiverSettingsView()
        {
            var root = new StackPanel { Margin = new Thickness(20) };

            root.Children.Add(new TextBlock
            {
                Text = ResourceProvider.GetString("LOCQuiverSettingsFolderHeader"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4),
            });
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

            var syncLastPlayed = new CheckBox
            {
                Content = ResourceProvider.GetString("LOCQuiverSettingsSyncLastPlayed"),
                Margin = new Thickness(0, 16, 0, 4),
            };
            syncLastPlayed.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(QuiverSettingsViewModel.SyncLastPlayed)) { Mode = BindingMode.TwoWay });
            root.Children.Add(syncLastPlayed);
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsSyncLastPlayedHint")));

            var makePortable = new CheckBox
            {
                Content = ResourceProvider.GetString("LOCQuiverSettingsMakePortable"),
                Margin = new Thickness(0, 16, 0, 4),
            };
            makePortable.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(QuiverSettingsViewModel.MakePortable)) { Mode = BindingMode.TwoWay });
            root.Children.Add(makePortable);
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsMakePortableHint")));

            var ludusaviBackup = new CheckBox
            {
                Content = ResourceProvider.GetString("LOCQuiverSettingsLudusaviBackup"),
                Margin = new Thickness(0, 16, 0, 4),
            };
            ludusaviBackup.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(QuiverSettingsViewModel.LudusaviBackup)) { Mode = BindingMode.TwoWay });
            root.Children.Add(ludusaviBackup);
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsLudusaviBackupHint")));

            root.Children.Add(new TextBlock
            {
                Text = ResourceProvider.GetString("LOCQuiverSettingsFeaturesHeader"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 20, 0, 4),
            });
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsFeaturesHint")));
            root.Children.Add(BuildFeatureList());

            root.Children.Add(new TextBlock
            {
                Text = ResourceProvider.GetString("LOCQuiverSettingsHowItWorksHeader"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 20, 0, 4),
            });
            root.Children.Add(Hint(ResourceProvider.GetString("LOCQuiverSettingsHowItWorks")));

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = root,
            };
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
