using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

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
            _currentUser = currentUser;

            // ENHANCED: Ensure the required project is available
            _projects = EnsureProjectAvailable(projects, timeEntry.ProjectId);

            ProjectComboBox.Loaded += ProjectComboBox_Loaded;
            LoadData();
            SetupLockInfo();
        }

        private void LoadData()
        {
            System.Diagnostics.Debug.WriteLine("=== EditTimeEntryDialog LoadData Debug ===");
            System.Diagnostics.Debug.WriteLine($"Original ProjectId: {_originalTimeEntry.ProjectId}");

            // Set up projects combo box
            ProjectComboBox.ItemsSource = _projects;

            // Populate form with current values
            DatePicker.SelectedDate = _originalTimeEntry.Date;
            HoursTextBox.Text = _originalTimeEntry.Hours.ToString("0");
            MinutesTextBox.Text = _originalTimeEntry.Minutes.ToString("0");
            CommentsTextBox.Text = _originalTimeEntry.Comments ?? string.Empty;

            // Set category radio button
            switch (_originalTimeEntry.Category)
            {
                case TimeEntryCategory.NonChargeable:
                    NonChargeableRadioButton.IsChecked = true;
                    break;
                case TimeEntryCategory.Speculative:
                    SpeculativeRadioButton.IsChecked = true;
                    break;
                case TimeEntryCategory.HourlyRate:
                    HourlyRateRadioButton.IsChecked = true;
                    break;
                default:
                    ChargeableRadioButton.IsChecked = true;
                    break;
            }

            // 🔥 AGGRESSIVE FIX: Use Timer for delayed selection
            if (_originalTimeEntry.Classification == TimeEntryClassification.Project &&
                _originalTimeEntry.ProjectId != Guid.Empty)
            {
                System.Diagnostics.Debug.WriteLine($"Setting up timer for project selection: {_originalTimeEntry.ProjectId}");

                // Wait 100ms for WPF to finish data binding
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };

                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    SelectProjectWithForce(_originalTimeEntry.ProjectId);
                };

                timer.Start();
            }
        }

        private void ProjectComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            // This fires when the ComboBox is fully loaded and ready
            if (_originalTimeEntry.Classification == TimeEntryClassification.Project &&
                _originalTimeEntry.ProjectId != Guid.Empty)
            {
                System.Diagnostics.Debug.WriteLine("ComboBox loaded, attempting selection...");
                SelectProjectWithForce(_originalTimeEntry.ProjectId);
            }
        }

        private List<Project> EnsureProjectAvailable(List<Project> projects, Guid requiredProjectId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"EnsureProjectAvailable: Looking for {requiredProjectId}");

                // Check if project already exists in the provided list
                var existingProject = projects?.FirstOrDefault(p => p.Id == requiredProjectId);
                if (existingProject != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Project found in provided list: {existingProject.Name}");
                    return projects;
                }

                System.Diagnostics.Debug.WriteLine("Project not in provided list - fetching from database...");

                // Project missing - fetch it from database
                var missingProject = ServiceLocator.ProjectService?.GetProject(requiredProjectId);
                if (missingProject != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Found missing project in database: {missingProject.Name} (Active: {missingProject.IsActive})");

                    // Add the missing project to the list
                    var updatedProjects = new List<Project>(projects ?? new List<Project>()) { missingProject };
                    return updatedProjects.OrderBy(p => p.Name).ToList();
                }

                System.Diagnostics.Debug.WriteLine($"❌ Project {requiredProjectId} not found in database either");
                return projects ?? new List<Project>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in EnsureProjectAvailable: {ex.Message}");
                return projects ?? new List<Project>();
            }
        }

        private void SelectProjectWithForce(Guid projectId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== SelectProjectWithForce {projectId} ===");

                var targetProject = _projects?.FirstOrDefault(p => p.Id == projectId);
                if (targetProject == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Project still not found after database fetch");
                    MessageBox.Show("The project for this time entry could not be loaded. The project may have been deleted.",
                                   "Project Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Target project found: {targetProject.Name}");

                // Simple selection - project should definitely be available now
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ProjectComboBox.SelectedItem = targetProject;
                    if (ProjectComboBox.SelectedItem == null)
                    {
                        // Fallback strategies
                        ProjectComboBox.SelectedValue = projectId;
                        if (ProjectComboBox.SelectedItem == null)
                        {
                            var index = _projects.IndexOf(targetProject);
                            if (index >= 0) ProjectComboBox.SelectedIndex = index;
                        }
                    }
                    System.Diagnostics.Debug.WriteLine($"Selection result: {ProjectComboBox.SelectedItem != null}");
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SelectProjectWithForce: {ex.Message}");
            }
        }


        private void SelectProject(Guid projectId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== SelectProject {projectId} ===");

                // Strategy 1: Try SelectedValue (most reliable when it works)
                ProjectComboBox.SelectedValue = projectId;

                if (ProjectComboBox.SelectedItem != null)
                {
                    var selected = (Project)ProjectComboBox.SelectedItem;
                    System.Diagnostics.Debug.WriteLine($"✅ SUCCESS (SelectedValue): {selected.Name}");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("❌ SelectedValue failed, trying SelectedItem...");

                // Strategy 2: Manual search and SelectedItem
                var targetProject = _projects.FirstOrDefault(p => p.Id == projectId);
                if (targetProject != null)
                {
                    ProjectComboBox.SelectedItem = targetProject;

                    if (ProjectComboBox.SelectedItem != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ SUCCESS (SelectedItem): {targetProject.Name}");
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine("❌ SelectedItem failed, trying SelectedIndex...");

                    // Strategy 3: Find index and use SelectedIndex
                    var index = _projects.IndexOf(targetProject);
                    if (index >= 0)
                    {
                        ProjectComboBox.SelectedIndex = index;
                        System.Diagnostics.Debug.WriteLine($"✅ SUCCESS (SelectedIndex {index}): {targetProject.Name}");
                        return;
                    }
                }

                System.Diagnostics.Debug.WriteLine("❌ ALL STRATEGIES FAILED");
                System.Diagnostics.Debug.WriteLine("Available projects in combo box:");

                if (_projects != null)
                {
                    for (int i = 0; i < Math.Min(_projects.Count, 5); i++)
                    {
                        var p = _projects[i];
                        System.Diagnostics.Debug.WriteLine($"  [{i}] {p.Id}: {p.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SelectProject: {ex.Message}");
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
                ChargeableRadioButton.IsEnabled = false;
                NonChargeableRadioButton.IsEnabled = false;
                SpeculativeRadioButton.IsEnabled = false;
                HourlyRateRadioButton.IsEnabled = false;
                SaveButton.IsEnabled = false;
            }
        }


        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"=== SAVE BUTTON CLICKED ===");
            System.Diagnostics.Debug.WriteLine($"Time Entry ID: {_originalTimeEntry.IdGuid}");

            if (ValidateInput())
            {
                System.Diagnostics.Debug.WriteLine("✅ Input validation passed");

                // NEW: Add reconciliation validation BEFORE creating UpdatedTimeEntry
                System.Diagnostics.Debug.WriteLine("Starting reconciliation validation...");

                var reconciliationValidation = ReconciliationValidationHelper.ValidateTimeEntryModification(
                    _originalTimeEntry.IdGuid, ServiceLocator.DataverseConnector);

                System.Diagnostics.Debug.WriteLine($"Reconciliation result: IsValid={reconciliationValidation.IsValid}, Level={reconciliationValidation.Level}");
                System.Diagnostics.Debug.WriteLine($"Reconciliation message: {reconciliationValidation.Message}");

                if (!reconciliationValidation.IsValid)
                {
                    System.Diagnostics.Debug.WriteLine("🔒 BLOCKING EDIT - showing error message");
                    MessageBox.Show(reconciliationValidation.Message, "Cannot Modify Time Entry",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return; // Exit early - don't create UpdatedTimeEntry or close dialog
                }

                // Show warning if there's a reconciliation concern
                if (reconciliationValidation.Level == ReconciliationValidationLevel.Warning)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ SHOWING WARNING to user");
                    var result = MessageBox.Show($"{reconciliationValidation.Message}\n\nDo you want to continue?",
                        "Reconciliation Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                    {
                        System.Diagnostics.Debug.WriteLine("❌ User chose NOT to continue");
                        return; // Exit early - user chose not to continue
                    }

                    System.Diagnostics.Debug.WriteLine("✅ User chose to continue despite warning");
                }

                System.Diagnostics.Debug.WriteLine("✅ Reconciliation validation passed - creating UpdatedTimeEntry");

                // EXISTING: Create updated time entry (unchanged)
                var selectedCategory =
                    NonChargeableRadioButton.IsChecked == true ? TimeEntryCategory.NonChargeable :
                    SpeculativeRadioButton.IsChecked == true   ? TimeEntryCategory.Speculative :
                    HourlyRateRadioButton.IsChecked  == true   ? TimeEntryCategory.HourlyRate :
                    TimeEntryCategory.Chargeable;

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
                    ProjectName = ((Project)ProjectComboBox.SelectedItem).Name,
                    Category = selectedCategory
                };

                System.Diagnostics.Debug.WriteLine("✅ UpdatedTimeEntry created - closing dialog");
                DialogResult = true;
                Close();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("❌ Input validation failed");
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