using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;
using DHA.Leave.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xrm.Sdk;

namespace DHA.DSTC.WPF
{
    /// <summary>
    /// "Leave" tab: request leave, see balance and requests, cancel (DHA-9 phase B).
    /// Approvals and admin come in later slices - see docs/DHA-9-leave-plan.md.
    /// </summary>
    public partial class LeaveView : UserControl
    {
        private LeaveService _leaveService;
        private WorkingDay[] _pattern = LeaveDayCalculator.DefaultPattern();
        private HashSet<DateTime> _bankHolidays = new HashSet<DateTime>();
        private List<LeaveType> _types = new List<LeaveType>();
        private bool _loaded;

        public LeaveView()
        {
            InitializeComponent();
            StartDatePicker.SelectedDate = DateTime.Today;
            EndDatePicker.SelectedDate = DateTime.Today;
        }

        private Guid EmployeeId => ServiceLocator.EffectiveUserId;

        private async void LeaveView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded || ServiceLocator.DataverseConnector == null)
                return;
            _loaded = true;
            _leaveService = new LeaveService(ServiceLocator.DataverseConnector);
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                var employeeId = EmployeeId;
                var today = DateTime.Today;
                var result = await Task.Run(() => new
                {
                    Types = _leaveService.GetLeaveTypes(),
                    Pattern = _leaveService.GetWorkingPattern(employeeId),
                    Holidays = _leaveService.GetBankHolidays(today.AddYears(-1), today.AddYears(2)),
                    Requests = _leaveService.GetMyRequests(employeeId, today.AddYears(-1))
                });

                _types = result.Types;
                _pattern = result.Pattern;
                _bankHolidays = result.Holidays;

                var selectedType = (LeaveTypeComboBox.SelectedItem as LeaveType)?.Id;
                LeaveTypeComboBox.ItemsSource = _types;
                LeaveTypeComboBox.SelectedItem = _types.FirstOrDefault(t => t.Id == selectedType) ?? _types.FirstOrDefault();

                RequestsGrid.ItemsSource = result.Requests;
                var balance = await Task.Run(() => _leaveService.GetBalance(employeeId, today, result.Requests, result.Types));
                ShowBalance(balance);
                UpdatePreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load your leave: {ex.Message}", "Leave", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowBalance(LeaveBalance balance)
        {
            if (balance.EntitlementHours == 0m && balance.YearStart == default(DateTime))
            {
                BalanceTitleText.Text = "My Balance";
                PendingText.Text = "No entitlement has been set up for you yet - speak to HR.";
                return;
            }

            BalanceTitleText.Text = $"My Balance ({balance.YearStart:MMM yyyy} – {balance.YearStart.AddYears(1).AddDays(-1):MMM yyyy})";
            EntitlementText.Text = FormatDays(balance.EntitlementHours);
            TakenText.Text = FormatDays(balance.TakenHours);
            BookedText.Text = FormatDays(balance.BookedHours);
            RemainingText.Text = FormatDays(balance.RemainingHours);
            RemainingText.Foreground = balance.RemainingHours < 0
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x04, 0x78, 0x57));
            PendingText.Text = balance.PendingHours > 0 ? $"{FormatDays(balance.PendingHours)} waiting for approval." : "";
        }

        private string FormatDays(decimal hours)
        {
            // Balances are held in hours; days use the colleague's usual full day
            decimal day = _pattern.Where(d => d.ExpectedHours > 0).Select(d => d.ExpectedHours).DefaultIfEmpty(LeaveDayCalculator.DefaultDayHours).Max();
            return $"{hours / day:0.##}d";
        }

        // ---------- Request form ----------

        private void Date_Changed(object sender, SelectionChangedEventArgs e) => OnInputsChanged(sender);
        private void Length_Changed(object sender, RoutedEventArgs e) => OnInputsChanged(sender);
        private void Time_Changed(object sender, TextChangedEventArgs e) => OnInputsChanged(sender);
        private void Type_Changed(object sender, SelectionChangedEventArgs e) => OnInputsChanged(sender);

        private void OnInputsChanged(object sender)
        {
            if (CustomTimesPanel == null || PreviewTotalText == null)
                return; // still initialising

            CustomTimesPanel.Visibility = CustomTimesRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            // Morning/afternoon only make sense on a single day
            if ((MorningRadio.IsChecked == true || AfternoonRadio.IsChecked == true) && StartDatePicker.SelectedDate.HasValue)
                EndDatePicker.SelectedDate = StartDatePicker.SelectedDate;
            else if (sender == StartDatePicker && StartDatePicker.SelectedDate > EndDatePicker.SelectedDate)
                EndDatePicker.SelectedDate = StartDatePicker.SelectedDate;

            UpdatePreview();
        }

        /// <summary>Works out the start/end times implied by the Length choice. Returns false with a message if invalid.</summary>
        private bool TryGetTimes(out TimeSpan? start, out TimeSpan? end, out string error)
        {
            start = end = null;
            error = null;
            var date = StartDatePicker.SelectedDate ?? DateTime.Today;
            var day = _pattern[LeaveDayCalculator.PatternIndex(date.DayOfWeek)];
            var half = TimeSpan.FromHours((double)(day.ExpectedHours / 2m));

            if (MorningRadio.IsChecked == true)
            {
                end = day.Start + half;
            }
            else if (AfternoonRadio.IsChecked == true)
            {
                start = day.Finish - half;
            }
            else if (CustomTimesRadio.IsChecked == true)
            {
                start = ParseTime(StartTimeTextBox.Text);
                end = ParseTime(EndTimeTextBox.Text);
                if (start == null || end == null)
                {
                    error = "Enter times as HH:mm, e.g. 11:00.";
                    return false;
                }
                if (StartDatePicker.SelectedDate == EndDatePicker.SelectedDate && end <= start)
                {
                    error = "The end time must be after the start time.";
                    return false;
                }
            }
            return true;
        }

        private static TimeSpan? ParseTime(string text)
        {
            return TimeSpan.TryParseExact((text ?? "").Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var ts) ? ts : (TimeSpan?)null;
        }

        private List<LeaveDay> CalculateDays(out string error)
        {
            error = null;
            if (!StartDatePicker.SelectedDate.HasValue || !EndDatePicker.SelectedDate.HasValue)
            {
                error = "Choose the dates.";
                return null;
            }
            if (EndDatePicker.SelectedDate < StartDatePicker.SelectedDate)
            {
                error = "The end date is before the start date.";
                return null;
            }
            if (!TryGetTimes(out var start, out var end, out error))
                return null;

            return LeaveDayCalculator.Calculate(StartDatePicker.SelectedDate.Value, EndDatePicker.SelectedDate.Value,
                start, end, _pattern, _bankHolidays);
        }

        private void UpdatePreview()
        {
            var days = CalculateDays(out var error);
            if (days == null)
            {
                PreviewTotalText.Text = error;
                PreviewDetailText.Text = "";
                SubmitButton.IsEnabled = false;
                return;
            }

            decimal total = days.Sum(d => d.Hours);
            PreviewTotalText.Text = days.Count == 0
                ? "No working days in this range."
                : $"{total:0.##} hours ({FormatDays(total)}) over {days.Count} working day{(days.Count == 1 ? "" : "s")}";
            var skippedHolidays = _bankHolidays.Count(h => h >= StartDatePicker.SelectedDate && h <= EndDatePicker.SelectedDate);
            PreviewDetailText.Text = string.Join(", ", days.Take(10).Select(d => $"{d.Date:ddd dd/MM} {d.Hours:0.##}h"))
                + (days.Count > 10 ? ", …" : "")
                + (skippedHolidays > 0 ? $"\nBank holidays in this range aren't counted ({skippedHolidays})." : "");
            SubmitButton.IsEnabled = days.Count > 0 && _leaveService != null;
        }

        private async void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            var days = CalculateDays(out var error);
            if (days == null || days.Count == 0)
                return;
            TryGetTimes(out var start, out var end, out error);

            var request = new LeaveRequest
            {
                EmployeeId = EmployeeId,
                LeaveTypeId = (LeaveTypeComboBox.SelectedItem as LeaveType)?.Id,
                StartDate = StartDatePicker.SelectedDate.Value,
                EndDate = EndDatePicker.SelectedDate.Value,
                StartTime = start?.ToString(@"hh\:mm"),
                EndTime = end?.ToString(@"hh\:mm"),
                RequesterComments = CommentsTextBox.Text
            };

            SubmitButton.IsEnabled = false;
            try
            {
                await Task.Run(() => _leaveService.SubmitRequest(request));
                CommentsTextBox.Clear();
                MessageBox.Show("Your leave request has been sent for approval.", "Leave requested", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadAsync();
            }
            catch (FaultException<OrganizationServiceFault> fault)
            {
                // Plug-in validation (e.g. overlapping leave) comes back as a fault with a readable message
                MessageBox.Show(fault.Detail.Message, "Leave not requested", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not request leave: {ex.Message}", "Leave", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdatePreview();
            }
        }

        // ---------- My requests ----------

        private void RequestsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CancelRequestButton.IsEnabled = (RequestsGrid.SelectedItem as LeaveRequest)?.CanCancel == true;
        }

        private async void CancelRequestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(RequestsGrid.SelectedItem is LeaveRequest request))
                return;

            string question = request.Status == LeaveRequestStatus.Approved
                ? $"Ask {request.ApproverName} to cancel your approved leave on {request.DatesText}?"
                : $"Cancel your leave request for {request.DatesText}?";
            if (MessageBox.Show(question, "Cancel leave", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                await Task.Run(() => _leaveService.CancelRequest(request));
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not cancel: {ex.Message}", "Leave", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    }
}
