
using DHA.DSTC.WPF.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static DHA.DSTC.WPF.MainWindow;

namespace DHA.DSTC.WPF
{
    public partial class TimeSlotEntryDialog : Window
    {
        private readonly TimeSlot _timeSlot;
        private readonly List<Project> _projects;
        private readonly TeamMember _currentUser;

        public TimeEntry ResultTimeEntry { get; private set; }

        public TimeSlotEntryDialog(TimeSlot timeSlot, List<Project> projects, TeamMember currentUser)
        {
            InitializeComponent();

            _timeSlot = timeSlot ?? throw new ArgumentNullException(nameof(timeSlot));
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
            _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

            InitializeDialog();
        }

        private void InitializeDialog()
        {
            try
            {
                // Set window title
                Title = $"Log Time: {_timeSlot.StartTime:HH:mm} - {_timeSlot.EndTime:HH:mm}";

                // Set time slot info
                TimeSlotLabel.Text = $"Time Slot: {_timeSlot.StartTime:HH:mm} - {_timeSlot.EndTime:HH:mm} " +
                    $"({MINUTES_PER_SLOT} minutes)";

                // Show existing time if any
                if (_timeSlot.IsOccupied)
                {
                    ExistingTimePanel.Visibility = Visibility.Visible;
                    ExistingTimeText.Text = $"Already logged: {FormatHoursMinutes(_timeSlot.LoggedHours)}";

                    if (_timeSlot.TimeEntries.Any())
                    {
                        var entriesText = string.Join("\n", _timeSlot.TimeEntries.Select(te =>
                            $"• {te.ProjectName}: {te.TotalTime} - {te.Comments}"));
                        ExistingEntriesText.Text = entriesText;
                    }
                }
                else
                {
                    ExistingTimePanel.Visibility = Visibility.Collapsed;
                }

                // Populate projects - filter to active projects only
                var activeProjects = _projects.Where(p => p.IsActive).OrderBy(p => p.Name).ToList();
                ProjectComboBox.ItemsSource = activeProjects;

                if (activeProjects.Any())
                {
                    ProjectComboBox.SelectedIndex = 0; // Select first project by default
                }

                // Set default values
                HoursTextBox.Text = "0";
                MinutesTextBox.Text = "15"; // Default to full 15-minute slot

                // Set default category to Chargeable (matches your enum)
                ChargeableRadioButton.IsChecked = true;

                // Set focus to project selection
                ProjectComboBox.Focus();

                // Set up quick buttons for common time intervals
                SetupQuickTimeButtons();

                // Update the total hours display
                UpdateTotalHoursDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing dialog: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetupQuickTimeButtons()
        {
            // Add quick time entry buttons
            var quickTimes = new[]
            {
                (0, 15, "15min"),
                (0, 30, "30min"),
                (1, 0, "1hr"),
                (1, 30, "1.5hr"),
                (2, 0, "2hr")
            };

            QuickTimePanel.Children.Clear();

            foreach (var (hours, minutes, label) in quickTimes)
            {
                var button = new Button
                {
                    Content = label,
                    Margin = new Thickness(5, 2, 5, 2),
                    Padding = new Thickness(8, 4, 8, 4),
                    MinWidth = 50,
                    Style = (Style)FindResource("SecondaryButtonStyle")
                };

                button.Click += (s, e) => SetTimeValues(hours, minutes);
                QuickTimePanel.Children.Add(button);
            }
        }

        private void SetTimeValues(int hours, int minutes)
        {
            HoursTextBox.Text = hours.ToString();
            MinutesTextBox.Text = minutes.ToString();
            UpdateTotalHoursDisplay();
        }

        private void UpdateTotalHoursDisplay()
        {
            try
            {
                if (int.TryParse(HoursTextBox.Text, out int hours) &&
                    int.TryParse(MinutesTextBox.Text, out int minutes))
                {
                    var totalHours = hours + (minutes / 60.0m);
                    TotalHoursLabel.Text = $"= {FormatHoursMinutes(totalHours)}";
                }
                else
                {
                    TotalHoursLabel.Text = "= 0m";
                }
            }
            catch
            {
                TotalHoursLabel.Text = "= 0m";
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateAndCreateTimeEntry())
            {
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ResultTimeEntry = null;
            DialogResult = false;
            Close();
        }

        private static string FormatHoursMinutes(decimal totalHours)
        {
            if (totalHours == 0) return "0m";
            int wholeHours = (int)Math.Floor(totalHours);
            int minutes = (int)Math.Round((totalHours - wholeHours) * 60);
            if (wholeHours == 0) return $"{minutes}m";
            if (minutes == 0) return $"{wholeHours}h";
            return $"{wholeHours}h {minutes}m";
        }

        private bool ValidateAndCreateTimeEntry()
        {
            try
            {
                // Validate project selection
                if (ProjectComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Please select a project.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ProjectComboBox.Focus();
                    return false;
                }

                // Validate time values
                if (!int.TryParse(HoursTextBox.Text, out int hours) || hours < 0 || hours > 12)
                {
                    MessageBox.Show("Hours must be a number between 0 and 12.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    HoursTextBox.Focus();
                    return false;
                }

                if (!int.TryParse(MinutesTextBox.Text, out int minutes) || minutes < 0 || minutes > 59)
                {
                    MessageBox.Show("Minutes must be a number between 0 and 59.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    MinutesTextBox.Focus();
                    return false;
                }

                // Validate that some time is entered
                if (hours == 0 && minutes == 0)
                {
                    MessageBox.Show("Please enter some time to log.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    MinutesTextBox.Focus();
                    return false;
                }

                // Validate comments if required
                if (string.IsNullOrWhiteSpace(CommentsTextBox.Text))
                {
                    var result = MessageBox.Show("No comments entered. Time entries work best with descriptions.\n\n" +
                        "Would you like to add a comment?", "Comments Recommended",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        CommentsTextBox.Focus();
                        return false;
                    }
                }

                // Create the time entry using your existing model structure
                var selectedProject = (Project)ProjectComboBox.SelectedItem;

                ResultTimeEntry = new TimeEntry
                {
                    Date = DateTime.Today, // Always log for today when using time slots
                    Hours = hours, // Your model expects decimal hours
                    Minutes = minutes, // Your model has separate minutes
                    Comments = CommentsTextBox.Text?.Trim() ??
                        $"Time logged via slot: {_timeSlot.StartTime:HH:mm}-{_timeSlot.EndTime:HH:mm}",
                    ProjectId = selectedProject.Id,
                    ProjectName = selectedProject.Name,
                    TeamMemberId = _currentUser.Id,

                    // Set category based on radio button selection using your existing enum
                    Category = GetSelectedCategory(),

                    // Set classification to Project (since we're logging against projects)
                    Classification = TimeEntryClassification.Project
                };

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating time entry: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Get the selected category from radio buttons
        /// </summary>
        private TimeEntryCategory GetSelectedCategory()
        {
            if (ChargeableRadioButton.IsChecked == true)
                return TimeEntryCategory.Chargeable;
            else if (NonChargeableRadioButton.IsChecked == true)
                return TimeEntryCategory.NonChargeable;
            else if (SpeculativeRadioButton.IsChecked == true)
                return TimeEntryCategory.Speculative;
            else
                return TimeEntryCategory.Chargeable; // Default fallback
        }

        private void ProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Auto-populate comments with project name if comments are empty
            if (string.IsNullOrWhiteSpace(CommentsTextBox.Text) && ProjectComboBox.SelectedItem is Project project)
            {
                CommentsTextBox.Text = $"Work on {project.Name}";
                CommentsTextBox.SelectAll();
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Handle keyboard shortcuts
            if (e.Key == System.Windows.Input.Key.Enter &&
                (e.KeyboardDevice.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                // Ctrl+Enter to save
                SaveButton_Click(sender, new RoutedEventArgs());
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                // Escape to cancel
                CancelButton_Click(sender, new RoutedEventArgs());
            }
        }

        private void MinutesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Auto-convert minutes over 60 to hours
            if (int.TryParse(MinutesTextBox.Text, out int minutes) && minutes >= 60)
            {
                var extraHours = minutes / 60;
                var remainingMinutes = minutes % 60;

                if (int.TryParse(HoursTextBox.Text, out int currentHours))
                {
                    HoursTextBox.Text = (currentHours + extraHours).ToString();
                    MinutesTextBox.Text = remainingMinutes.ToString();
                }
            }

            // Update the total hours display
            UpdateTotalHoursDisplay();
        }

        private void HoursTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateTotalHoursDisplay();
        }

        // Add the constant that was missing
        private const int MINUTES_PER_SLOT = 15;
    }
}