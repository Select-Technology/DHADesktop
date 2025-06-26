using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Utilities;

namespace DHA.DSTC.WPF
{
    public partial class EditTimeEntryDialog : Window
    {
        private readonly TimeEntry _originalTimeEntry;
        private readonly List<Project> _projects;
        private readonly TeamMember _currentUser;

        public TimeEntry UpdatedTimeEntry { get; private set; }

        public EditTimeEntryDialog(TimeEntry timeEntry, List<Project> projects, TeamMember currentUser)
        {
            InitializeComponent();

            _originalTimeEntry = timeEntry;
            _projects = projects;
            _currentUser = currentUser;

            LoadData();
            SetupLockInfo();
        }

        private void LoadData()
        {
            // Set up projects combo box
            ProjectComboBox.ItemsSource = _projects;

            // Populate form with current values
            DatePicker.SelectedDate = _originalTimeEntry.Date;
            HoursTextBox.Text = _originalTimeEntry.Hours.ToString("0");
            MinutesTextBox.Text = _originalTimeEntry.Minutes.ToString("0");
            CommentsTextBox.Text = _originalTimeEntry.Comments ?? string.Empty;

            // Select the current project
            var currentProject = _projects.FirstOrDefault(p => p.Id == _originalTimeEntry.ProjectId);
            if (currentProject != null)
            {
                ProjectComboBox.SelectedItem = currentProject;
            }
        }

        private void SetupLockInfo()
        {
            var lockDate = _originalTimeEntry.LockDate;
            var timeRemaining = lockDate - DateTime.Now;

            if (timeRemaining.TotalHours > 0)
            {
                if (timeRemaining.TotalDays >= 1)
                {
                    LockInfoText.Text = $"This time entry will be locked on {lockDate:dddd, dd MMMM yyyy} at 12:00 noon. " +
                                       $"You have approximately {timeRemaining.Days} day(s) remaining to make changes.";
                }
                else
                {
                    LockInfoText.Text = $"This time entry will be locked on {lockDate:dddd, dd MMMM yyyy} at 12:00 noon. " +
                                       $"You have approximately {timeRemaining.Hours} hour(s) remaining to make changes.";
                }

                // Change to warning colour if less than 24 hours
                if (timeRemaining.TotalHours < 24)
                {
                    LockInfoBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226));
                    LockInfoBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                }
            }
            else
            {
                LockInfoText.Text = $"This time entry was locked on {lockDate:dddd, dd MMMM yyyy} at 12:00 noon and can no longer be edited.";
                LockInfoBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226));
                LockInfoBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));

                // Disable all controls
                DatePicker.IsEnabled = false;
                ProjectComboBox.IsEnabled = false;
                HoursTextBox.IsEnabled = false;
                MinutesTextBox.IsEnabled = false;
                CommentsTextBox.IsEnabled = false;
                SaveButton.IsEnabled = false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput())
            {
                // Create updated time entry
                UpdatedTimeEntry = new TimeEntry
                {
                    Id = _originalTimeEntry.Id,
                    IdGuid = _originalTimeEntry.IdGuid,
                    Date = DatePicker.SelectedDate ?? DateTime.Today,
                    Hours = decimal.Parse(HoursTextBox.Text.DefaultIfEmpty("0")),
                    Minutes = int.Parse(MinutesTextBox.Text.DefaultIfEmpty("0")),
                    Comments = CommentsTextBox.Text,
                    ProjectId = ((Project)ProjectComboBox.SelectedItem).Id,
                    TeamMemberId = _currentUser.Id,
                    ProjectName = ((Project)ProjectComboBox.SelectedItem).Name
                };

                // Double-check validation one more time
                if (!TimeEntryValidationHelper.CanEditTimeEntry(UpdatedTimeEntry.Date))
                {
                    MessageBox.Show(
                        "This time entry can no longer be edited as the edit window has expired.",
                        "Edit Window Expired",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool ValidateInput()
        {
            // Validate date
            if (!DatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Please select a date.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DatePicker.Focus();
                return false;
            }

            // Validate project
            if (ProjectComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a project.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ProjectComboBox.Focus();
                return false;
            }

            // Validate hours
            if (!decimal.TryParse(HoursTextBox.Text.DefaultIfEmpty("0"), out decimal hours) || hours < 0)
            {
                MessageBox.Show("Please enter a valid number of hours (0 or greater).", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                HoursTextBox.Focus();
                return false;
            }

            // Validate minutes
            if (!int.TryParse(MinutesTextBox.Text.DefaultIfEmpty("0"), out int minutes) || minutes < 0 || minutes >= 60)
            {
                MessageBox.Show("Please enter valid minutes (0-59).", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MinutesTextBox.Focus();
                return false;
            }

            // Validate total time
            if (hours == 0 && minutes == 0)
            {
                MessageBox.Show("Please enter either hours or minutes.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                HoursTextBox.Focus();
                return false;
            }

            // Validate future dates
            if (DatePicker.SelectedDate.Value.Date > DateTime.Today)
            {
                MessageBox.Show("Cannot enter time for future dates.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DatePicker.Focus();
                return false;
            }

            return true;
        }
    }

    // Extension method to handle empty strings
    public static class StringExtensions
    {
        public static string DefaultIfEmpty(this string value, string defaultValue = "")
        {
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }
    }
}