using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;

namespace DHA.DSTC.WPF
{
    public partial class EditQuoteTimeEntryDialog : Window
    {
        private readonly TimeEntry _originalTimeEntry;
        private readonly List<Quote> _quotes;
        private readonly TeamMember _currentUser;
        private readonly ColleagueConfiguration _userConfig;

        public TimeEntry UpdatedTimeEntry { get; private set; }

        private static string FormatHoursMinutes(decimal totalHours)
        {
            if (totalHours == 0) return "0m";
            int wholeHours = (int)Math.Floor(totalHours);
            int minutes = (int)Math.Round((totalHours - wholeHours) * 60);
            if (wholeHours == 0) return $"{minutes}m";
            if (minutes == 0) return $"{wholeHours}h";
            return $"{wholeHours}h {minutes}m";
        }

        public EditQuoteTimeEntryDialog(TimeEntry timeEntry, List<Quote> quotes, TeamMember currentUser)
        {
            InitializeComponent();

            _originalTimeEntry = timeEntry;
            _quotes = quotes;
            _currentUser = currentUser;
            _userConfig = LoadUserConfig(currentUser?.Id ?? Guid.Empty);

            LoadData();
            SetupLockInfo();
        }

        private static ColleagueConfiguration LoadUserConfig(Guid userId)
        {
            try
            {
                if (userId != Guid.Empty && ServiceLocator.ColleagueConfigurationService != null)
                {
                    return ServiceLocator.ColleagueConfigurationService.GetColleagueConfiguration(userId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EditQuoteTimeEntryDialog: failed to load colleague config: {ex.Message}");
            }
            return ColleagueConfiguration.CreateDefault();
        }

        private void LoadData()
        {
            // Set up quotes combo box
            QuoteComboBox.ItemsSource = _quotes;

            // Debug logging - essential for troubleshooting
            System.Diagnostics.Debug.WriteLine("=== EditQuoteTimeEntryDialog LoadData Debug ===");
            System.Diagnostics.Debug.WriteLine($"Original TimeEntry ID: {_originalTimeEntry.Id}");
            System.Diagnostics.Debug.WriteLine($"Original QuoteId: {_originalTimeEntry.QuoteId}");
            System.Diagnostics.Debug.WriteLine($"Original Classification: {_originalTimeEntry.Classification}");
            System.Diagnostics.Debug.WriteLine($"Available quotes count: {_quotes?.Count ?? 0}");

            if (_quotes != null && _quotes.Any())
            {
                System.Diagnostics.Debug.WriteLine("Available quote IDs:");
                foreach (var q in _quotes.Take(5)) // Log first 5 quotes
                {
                    System.Diagnostics.Debug.WriteLine($"  - {q.Id}: {q.Name}");
                }
            }

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

            // Select the current charge band
            SelectChargeBand(_originalTimeEntry.ChargeBand);
            UpdateChargeBandRateLabel();

            // 🔥 CRITICAL: Only try to select quote if this is a quote entry AND QuoteId is valid
            if (_originalTimeEntry.Classification == TimeEntryClassification.Quote &&
                _originalTimeEntry.QuoteId != Guid.Empty)
            {
                System.Diagnostics.Debug.WriteLine($"Attempting to select quote with ID: {_originalTimeEntry.QuoteId}");

                // Method 1: Try SelectedValue first (most reliable for IDs)
                QuoteComboBox.SelectedValue = _originalTimeEntry.QuoteId;

                // Check if selection worked
                if (QuoteComboBox.SelectedItem != null)
                {
                    System.Diagnostics.Debug.WriteLine("✅ SUCCESS: SelectedValue method worked");
                    var selectedQuote = (Quote)QuoteComboBox.SelectedItem;
                    System.Diagnostics.Debug.WriteLine($"Selected quote: {selectedQuote.Name}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ FAILED: SelectedValue method failed, trying SelectedItem");

                    // Method 2: Fallback to SelectedItem approach
                    var currentQuote = _quotes.FirstOrDefault(q => q.Id == _originalTimeEntry.QuoteId);
                    if (currentQuote != null)
                    {
                        QuoteComboBox.SelectedItem = currentQuote;
                        System.Diagnostics.Debug.WriteLine($"✅ FALLBACK SUCCESS: Found quote {currentQuote.Name}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("❌ FALLBACK FAILED: Quote not found in collection");
                        System.Diagnostics.Debug.WriteLine("This suggests either:");
                        System.Diagnostics.Debug.WriteLine("1. QuoteId in TimeEntry is wrong/empty");
                        System.Diagnostics.Debug.WriteLine("2. Quotes collection doesn't contain the needed quote");
                        System.Diagnostics.Debug.WriteLine("3. Data loading issue from Dataverse");
                    }
                }
            }
            else if (_originalTimeEntry.Classification == TimeEntryClassification.Project)
            {
                System.Diagnostics.Debug.WriteLine("This is a Project entry - wrong dialog opened!");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"No quote selection needed - QuoteId: {_originalTimeEntry.QuoteId}");
            }

            System.Diagnostics.Debug.WriteLine("=== End LoadData Debug ===");
        }

        private void SetupLockInfo()
        {
            var lockDate = _originalTimeEntry.LockDate;
            var timeRemaining = lockDate - DateTime.Now;

            if (timeRemaining.TotalHours > 0)
            {
                if (timeRemaining.TotalDays >= 1)
                {
                    LockInfoText.Text = $"This quote time entry will be locked on {lockDate:dddd, dd MMMM yyyy} at 12:00 noon. " +
                                       $"You have approximately {timeRemaining.Days} day(s) remaining to make changes.";
                }
                else
                {
                    LockInfoText.Text = $"This quote time entry will be locked on {lockDate:dddd, dd MMMM yyyy} at 12:00 noon. " +
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
                LockInfoText.Text = $"This quote time entry was locked on {lockDate:dddd, dd MMMM yyyy} at 12:00 noon and can no longer be edited.";
                LockInfoBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226));
                LockInfoBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));

                // Disable all controls
                DatePicker.IsEnabled = false;
                QuoteComboBox.IsEnabled = false;
                HoursTextBox.IsEnabled = false;
                MinutesTextBox.IsEnabled = false;
                CommentsTextBox.IsEnabled = false;
                ChargeableRadioButton.IsEnabled = false;
                NonChargeableRadioButton.IsEnabled = false;
                SpeculativeRadioButton.IsEnabled = false;
                HourlyRateRadioButton.IsEnabled = false;
                ChargeBandComboBox.IsEnabled = false;
                SaveButton.IsEnabled = false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput())
            {
                // Create updated time entry
                var selectedQuote = (Quote)QuoteComboBox.SelectedItem;

                var selectedCategory =
                    NonChargeableRadioButton.IsChecked == true ? TimeEntryCategory.NonChargeable :
                    SpeculativeRadioButton.IsChecked  == true   ? TimeEntryCategory.Speculative :
                    HourlyRateRadioButton.IsChecked   == true   ? TimeEntryCategory.HourlyRate :
                    TimeEntryCategory.Chargeable;

                UpdatedTimeEntry = new TimeEntry
                {
                    Id = _originalTimeEntry.Id,
                    IdGuid = _originalTimeEntry.IdGuid,
                    Date = DatePicker.SelectedDate ?? DateTime.Today,
                    Hours = decimal.Parse(HoursTextBox.Text.DefaultIfEmpty("0")),
                    Minutes = int.Parse(MinutesTextBox.Text.DefaultIfEmpty("0")),
                    Comments = CommentsTextBox.Text,
                    QuoteId = selectedQuote.Id,
                    TeamMemberId = _currentUser.Id,
                    QuoteName = selectedQuote.Name,
                    QuoteNumber = selectedQuote.QuoteNumber,
                    ClientName = selectedQuote.Client,
                    Classification = TimeEntryClassification.Quote,
                    Category = selectedCategory,
                    ChargeBand = GetSelectedChargeBand(),
                    ChargeRateValue = _userConfig?.GetRateForBand(GetSelectedChargeBand()) ?? 0m
                };

                // Double-check validation one more time
                if (!TimeEntryValidationHelper.CanEditTimeEntry(_originalTimeEntry.CreatedDate))
                {
                    MessageBox.Show(
                        "This quote time entry can no longer be edited as the edit window has expired.",
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

        private void ChargeBandComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateChargeBandRateLabel();
        }

        private ChargeBand GetSelectedChargeBand()
        {
            var item = ChargeBandComboBox?.SelectedItem as System.Windows.Controls.ComboBoxItem;
            if (item?.Tag != null
                && int.TryParse(item.Tag.ToString(), out int value)
                && Enum.IsDefined(typeof(ChargeBand), value))
            {
                return (ChargeBand)value;
            }
            return ChargeBand.RateA;
        }

        private void SelectChargeBand(ChargeBand band)
        {
            if (ChargeBandComboBox == null) return;

            foreach (var obj in ChargeBandComboBox.Items)
            {
                if (obj is System.Windows.Controls.ComboBoxItem item
                    && int.TryParse(item.Tag?.ToString(), out int value)
                    && value == (int)band)
                {
                    ChargeBandComboBox.SelectedItem = item;
                    return;
                }
            }
            ChargeBandComboBox.SelectedIndex = 0; // Fall back to Rate A
        }

        private void UpdateChargeBandRateLabel()
        {
            if (ChargeBandRateLabel == null) return;

            var rate = _userConfig?.GetRateForBand(GetSelectedChargeBand()) ?? 0m;
            ChargeBandRateLabel.Text = rate > 0m ? $"{rate:C}/hr" : string.Empty;
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

            // Validate quote selection (CORRECTED - using QuoteComboBox instead of ProjectComboBox)
            if (QuoteComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a quote.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                QuoteComboBox.Focus();
                return false;
            }

            // Validate hours
            if (!decimal.TryParse(HoursTextBox.Text, out decimal hours) || hours < 0)
            {
                MessageBox.Show("Hours must be a valid number 0 or greater.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                HoursTextBox.Focus();
                return false;
            }

            // Validate minutes
            if (!int.TryParse(MinutesTextBox.Text, out int minutes) || minutes < 0 || minutes >= 60)
            {
                MessageBox.Show("Minutes must be between 0 and 59.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MinutesTextBox.Focus();
                return false;
            }

            // NEW: Validate single entry doesn't exceed 16 hours
            decimal totalHoursForEntry = hours + (minutes / 60.0m);
            if (totalHoursForEntry > 16.0m)
            {
                MessageBox.Show("Individual time entries cannot exceed 16 hours.\n\n" +
                               "💡 For longer periods, please split across multiple entries or days.",
                               "Entry Too Large",
                               MessageBoxButton.OK,
                               MessageBoxImage.Warning);
                HoursTextBox.Focus();
                return false;
            }

            // Validate that some time is entered
            if (hours == 0 && minutes == 0)
            {
                MessageBox.Show("Please enter some time.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                HoursTextBox.Focus();
                return false;
            }

            // NEW: Validate 24-hour daily limit for quote time entries
            var entryDate = DatePicker.SelectedDate.Value;

            // Get the main window instance to access TimeEntries
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                var validationResult = DailyTimeValidationHelper.ValidateDailyTimeLimit(
                    entryDate,
                    hours,
                    minutes,
                    _currentUser.Id,
                    mainWindow.TimeEntries,
                    _originalTimeEntry.IdGuid); // Exclude the current entry being edited

                if (!validationResult.IsValid)
                {
                    var result = MessageBox.Show(
                        validationResult.Message + "\n\nWould you like to see the suggested maximum?",
                        "Daily Time Limit Exceeded",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        var suggestion = DailyTimeValidationHelper.SuggestMaximumSafeEntry(
                            entryDate, _currentUser.Id, mainWindow.TimeEntries, _originalTimeEntry.IdGuid);

                        MessageBox.Show(
                            suggestion.Message,
                            "Suggested Maximum Entry",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        // Pre-fill with suggested values
                        HoursTextBox.Text = suggestion.MaxHours.ToString();
                        MinutesTextBox.Text = suggestion.MaxMinutes.ToString();
                    }
                    return false;
                }

                // Show warning if approaching limit
                var dailySummary = DailyTimeValidationHelper.GetDailyTimeSummary(
                    entryDate, _currentUser.Id, mainWindow.TimeEntries, _originalTimeEntry.IdGuid);

                if (dailySummary.IsNearLimit && !dailySummary.IsAtLimit)
                {
                    decimal newTotal = dailySummary.TotalHoursLogged + totalHoursForEntry;
                    MessageBox.Show(
                        $"⚠️ Approaching daily limit\n\n" +
                        $"After this edit: {FormatHoursMinutes(newTotal)} / 24h\n" +
                        $"Remaining: {FormatHoursMinutes(Math.Max(0, 24.0m - newTotal))}",
                        "Near Daily Limit",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }

            // Validate comments
            if (string.IsNullOrWhiteSpace(CommentsTextBox.Text))
            {
                MessageBox.Show("Comments are required.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CommentsTextBox.Focus();
                return false;
            }

            return true;
        }

    }

}