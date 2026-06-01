using System.Windows;
using System.Windows.Controls;

namespace DHA.DSTC.WPF
{
    public static class InputDialog
    {
        public static string ShowDialog(string message, string title, string defaultValue = "")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 500,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.White
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Message text
            var messageLabel = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 20, 20, 10),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(messageLabel, 0);
            mainGrid.Children.Add(messageLabel);

            // Input textbox
            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(20, 10, 20, 10),
                Height = 25,
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(textBox, 1);
            mainGrid.Children.Add(textBox);

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20, 10, 20, 20)
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Height = 25,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 25,
                IsCancel = true
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            dialog.Content = mainGrid;

            string result = null;

            okButton.Click += (sender, e) =>
            {
                result = textBox.Text;
                dialog.DialogResult = true;
            };

            cancelButton.Click += (sender, e) =>
            {
                dialog.DialogResult = false;
            };

            // Handle Enter key
            textBox.KeyDown += (sender, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    result = textBox.Text;
                    dialog.DialogResult = true;
                }
            };

            // Focus the textbox and select all text
            dialog.Loaded += (sender, e) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

            bool? dialogResult = dialog.ShowDialog();
            return dialogResult == true ? result : null;
        }
    }
}