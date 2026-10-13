using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DHA.DSTC.WPF.Models;

namespace DHA.DSTC.WPF
{
    /// <summary>
    /// Deliberately hard-to-ignore red warning shown after time has been saved against a project
    /// that Finance has placed on stop (DHA-8). The time entry is already saved; this only warns.
    /// </summary>
    public static class ProjectOnStopWarningDialog
    {
        private static readonly SolidColorBrush Red = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
        private static readonly SolidColorBrush DarkRed = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
        private static readonly SolidColorBrush PaleRed = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));

        public static void Show(Window owner, Project project, string savedMessage)
        {
            var dialog = new Window
            {
                Title = Project.OnStopWarningMessage,
                Width = 480,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = PaleRed,
                BorderBrush = Red,
                BorderThickness = new Thickness(3),
                Owner = owner
            };

            var panel = new StackPanel();

            // Solid red header band
            var header = new Border
            {
                Background = Red,
                Padding = new Thickness(20, 14, 20, 14),
                Child = new TextBlock
                {
                    Text = "⛔ " + Project.OnStopWarningMessage.ToUpperInvariant(),
                    Foreground = Brushes.White,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            panel.Children.Add(header);

            panel.Children.Add(new TextBlock
            {
                Text = project != null ? $"{project.Number} - {project.Name}" : "",
                Foreground = DarkRed,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 16, 20, 6)
            });

            panel.Children.Add(new TextBlock
            {
                Text = savedMessage + "\n\nFinance has placed this project on stop. No further time should be spent on it.",
                Foreground = DarkRed,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 0, 20, 16)
            });

            var okButton = new Button
            {
                Content = "I understand",
                MinWidth = 120,
                Height = 32,
                Margin = new Thickness(20, 0, 20, 20),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Red,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                IsDefault = true,
                IsCancel = true
            };
            okButton.Click += (s, e) => dialog.Close();
            panel.Children.Add(okButton);

            dialog.Content = panel;
            dialog.ShowDialog();
        }
    }
}
