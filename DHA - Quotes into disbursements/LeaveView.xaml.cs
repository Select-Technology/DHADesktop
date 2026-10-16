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
using System.Windows.Media;
using Microsoft.Xrm.Sdk;

namespace DHA.DSTC.WPF
{
    /// <summary>
    /// "Leave" tab, My leave: request leave, see balance and requests, cancel (DHA-9 phase B).
    /// Parity plan stage 2 (E1-E7, E9-E12) - see docs/leave-desktop-parity-plan.md.
    /// </summary>
    public partial class LeaveView : UserControl
    {
        private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
        private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x04, 0x78, 0x57));
        private const string AnyType = "Any type";

        private LeaveService _leaveService;
        private WorkingDay[] _pattern = LeaveDayCalculator.DefaultPattern();
        private HashSet<DateTime> _bankHolidays = new HashSet<DateTime>();
        private List<(DateTime date, string name)> _companyHolidays = new List<(DateTime, string)>();
        private List<LeaveType> _types = new List<LeaveType>();
        private List<LeaveRequest> _requests = new List<LeaveRequest>();
        private Dictionary<int, LeaveBalance> _balances = new Dictionary<int, LeaveBalance>();
        private bool _loading;

        public LeaveView()
        {
            InitializeComponent();
            StartDatePicker.SelectedDate = DateTime.Today;
            EndDatePicker.SelectedDate = DateTime.Today;
            var year = DateTime.Today.Year;
            BalanceYearComboBox.ItemsSource = new[] { year - 1, year, year + 1 };
            BalanceYearComboBox.SelectedItem = year;
        }

        private Guid EmployeeId => ServiceLocator.EffectiveUserId;
        private LeaveType SelectedType => LeaveTypeComboBox.SelectedItem as LeaveType;

        // Reload each time the tab is shown: the user may have switched environment (DEV button) or user
        private async void LeaveView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible || _loading || ServiceLocator.DataverseConnector == null)
                return;
            _leaveService = _leaveService ?? new LeaveService(ServiceLocator.DataverseConnector);
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            _loading = true;
            try
            {
                var employeeId = EmployeeId;
                var today = DateTime.Today;
                var years = new[] { today.Year - 1, today.Year, today.Year + 1 };
                var result = await Task.Run(() =>
                {
                    var types = _leaveService.GetLeaveTypes();
                    // From 1 Jan last year, so last year's balance is complete
                    var requests = _leaveService.GetMyRequests(employeeId, new DateTime(today.Year - 1, 1, 1));
                    return new
                    {
                        Types = types,
                        Requests = requests,
                        Pattern = _leaveService.GetWorkingPattern(employeeId),
                        Holidays = _leaveService.GetCompanyHolidays(new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year + 2, 12, 31)),
                        Approvers = _leaveService.GetApprovers(employeeId),
                        Balances = years.ToDictionary(y => y, y => _leaveService.GetBalance(employeeId, y))
                    };
                });

                _types = result.Types;
                _requests = result.Requests;
                _pattern = result.Pattern;
                _companyHolidays = result.Holidays;
                _bankHolidays = new HashSet<DateTime>(result.Holidays.Select(h => h.date));
                _balances = result.Balances;

                var selectedType = SelectedType?.Id;
                LeaveTypeComboBox.ItemsSource = _types;
                LeaveTypeComboBox.SelectedItem = _types.FirstOrDefault(t => t.Id == selectedType) ?? _types.FirstOrDefault();

                var typeFilter = TypeFilter.SelectedItem as string;
                TypeFilter.ItemsSource = new[] { AnyType }.Concat(_types.Select(t => t.Name)).ToList();
                TypeFilter.SelectedItem = typeFilter != null && _types.Any(t => t.Name == typeFilter) ? typeFilter : AnyType;

                var (approver, additional) = result.Approvers;
                ApproverText.Text = string.IsNullOrEmpty(approver)
                    ? "No leave approver is set up for you - speak to HR."
                    : $"Approved by {approver}" + (string.IsNullOrEmpty(additional) ? "" : $" or {additional}");

                ShowBalance();
                ApplyFilters();
                ShowHolidays();
                OnInputsChanged(null);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load your leave: {ex.Message}", "Leave", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _loading = false;
            }
        }

        // ---------- Balance (E9) ----------

        private void BalanceYear_Changed(object sender, SelectionChangedEventArgs e) => ShowBalance();

        private void ShowBalance()
        {
            if (EntitlementText == null || !(BalanceYearComboBox.SelectedItem is int year))
                return;

            if (!_balances.TryGetValue(year, out var balance) || !balance.HasEntitlement)
            {
                EntitlementText.Text = TakenText.Text = BookedText.Text = RemainingText.Text = "–";
                RemainingText.Foreground = Green;
                BreakdownText.Text = "";
                PendingText.Text = _balances.Count == 0 ? "" : $"No entitlement has been set up for you for {year} yet - speak to HR.";
                return;
            }

            EntitlementText.Text = FormatDays(balance.EntitlementDays);
            TakenText.Text = FormatDays(balance.TakenDays);
            BookedText.Text = FormatDays(balance.BookedDays);
            RemainingText.Text = FormatDays(balance.RemainingDays);
            RemainingText.Foreground = balance.RemainingDays < 0 ? Red : Green;

            var parts = new List<string> { $"{FormatDays(balance.BaseDays)} for {year}" };
            if (balance.CarriedForwardDays != 0) parts.Add($"{FormatDays(balance.CarriedForwardDays)} carried forward");
            if (balance.AdjustmentDays != 0) parts.Add($"{FormatDays(balance.AdjustmentDays)} adjustment");
            BreakdownText.Text = "Entitlement: " + string.Join(" + ", parts)
                + (balance.CarriedToNextYearDays != 0 ? $". {FormatDays(balance.CarriedToNextYearDays)} carried into {year + 1}." : "");
            PendingText.Text = balance.PendingDays > 0 ? $"{FormatDays(balance.PendingDays)} of the booked days are waiting for approval." : "";
        }

        // Entitlement and balances are in days (HR decision 15 Oct 2026)
        private static string FormatDays(decimal days) => $"{days:0.##}d";

        // ---------- Request form (E1-E7) ----------

        private void Date_Changed(object sender, SelectionChangedEventArgs e) => OnInputsChanged(sender);
        private void Length_Changed(object sender, RoutedEventArgs e) => OnInputsChanged(sender);
        private void Time_Changed(object sender, TextChangedEventArgs e) => OnInputsChanged(sender);
        private void Type_Changed(object sender, SelectionChangedEventArgs e) => OnInputsChanged(sender);

        private void OnInputsChanged(object sender)
        {
            if (TimesPanel == null || PreviewTotalText == null)
                return; // still initialising

            // Each type is booked one way: Hours types (e.g. Medical) with times, the rest in full or half days
            var type = SelectedType;
            bool hours = type?.BookedInHours == true;
            TimesPanel.Visibility = hours ? Visibility.Visible : Visibility.Collapsed;
            DayLengthPanel.Visibility = hours ? Visibility.Collapsed : Visibility.Visible;
            TypePrivacyText.Text = type != null && !type.IsAbsence
                ? $"{type.Name} is approved like leave but doesn't go on your timesheet or use your balance."
                : "Only you, your approver and HR can see the type. Your timesheet just says \"Approved leave\".";

            // Morning/afternoon only make sense on a single day
            if (!hours && (MorningRadio.IsChecked == true || AfternoonRadio.IsChecked == true) && StartDatePicker.SelectedDate.HasValue)
                EndDatePicker.SelectedDate = StartDatePicker.SelectedDate;
            else if (sender == StartDatePicker && StartDatePicker.SelectedDate > EndDatePicker.SelectedDate)
                EndDatePicker.SelectedDate = StartDatePicker.SelectedDate;

            UpdatePreview();
        }

        /// <summary>Works out the start/end times implied by the type and Length. Returns false with a message if invalid.</summary>
        private bool TryGetTimes(out TimeSpan? start, out TimeSpan? end, out string error)
        {
            start = end = null;
            error = null;
            var date = StartDatePicker.SelectedDate ?? DateTime.Today;
            var day = _pattern[LeaveDayCalculator.PatternIndex(date.DayOfWeek)];
            var half = TimeSpan.FromHours((double)(day.ExpectedHours / 2m));

            if (SelectedType?.BookedInHours == true)
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
            else if (MorningRadio.IsChecked == true)
            {
                end = day.Start + half;
            }
            else if (AfternoonRadio.IsChecked == true)
            {
                start = day.Finish - half;
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
            if (SelectedType == null)
            {
                error = "Choose the type of request.";
                return null;
            }
            if (!StartDatePicker.SelectedDate.HasValue || !EndDatePicker.SelectedDate.HasValue)
            {
                error = "Choose the dates.";
                return null;
            }
            var from = StartDatePicker.SelectedDate.Value;
            var to = EndDatePicker.SelectedDate.Value;
            if (to < from)
            {
                error = "The end date is before the start date.";
                return null;
            }
            // Same rule as the server: each request belongs to one leave year
            if (LeaveDayCalculator.SpansLeaveYears(from, to))
            {
                error = "Leave can't run across 31 December. Please book it as two requests, one each side of 1 January.";
                return null;
            }
            if (!TryGetTimes(out var start, out var end, out error))
                return null;

            return LeaveDayCalculator.Calculate(from, to, start, end, _pattern, _bankHolidays);
        }

        private void UpdatePreview()
        {
            PreviewBalanceText.Text = "";
            var days = CalculateDays(out var error);
            if (days == null)
            {
                PreviewTotalText.Text = error;
                PreviewDetailText.Text = "";
                SubmitButton.IsEnabled = false;
                return;
            }

            var type = SelectedType;
            decimal hours = days.Sum(d => d.Hours);
            decimal dayCount = LeaveDayCalculator.DaysFor(days, _pattern);
            if (days.Count == 0)
                PreviewTotalText.Text = "No working days in this range.";
            else if (!type.IsAbsence)
                PreviewTotalText.Text = $"{dayCount:0.##} day{(dayCount == 1 ? "" : "s")} {type.Name.ToLowerInvariant()}";
            else
                PreviewTotalText.Text = $"{dayCount:0.##} day{(dayCount == 1 ? "" : "s")} of leave ({hours:0.##} hours on your timesheet)";

            // Balance now and after, for types that use the entitlement (pending counts as taken, so "now" includes it)
            if (type.DeductsFromEntitlement && days.Count > 0
                && _balances.TryGetValue(StartDatePicker.SelectedDate.Value.Year, out var balance) && balance.HasEntitlement)
            {
                var after = balance.RemainingDays - dayCount;
                PreviewBalanceText.Text = $"Remaining {balance.YearStart.Year}: {FormatDays(balance.RemainingDays)} now, {FormatDays(after)} after this request";
                PreviewBalanceText.Foreground = after < 0 ? Red : (Brush)FindResource("TextPrimaryBrush");
            }

            var holidaysInRange = _companyHolidays.Where(h => h.date >= StartDatePicker.SelectedDate && h.date <= EndDatePicker.SelectedDate).ToList();
            PreviewDetailText.Text = string.Join(", ", days.Take(10).Select(d => $"{d.Date:ddd dd/MM} {d.Hours:0.##}h"))
                + (days.Count > 10 ? ", …" : "")
                + (holidaysInRange.Count > 0 ? "\nNot counted (company holiday): " + string.Join(", ", holidaysInRange.Select(h => $"{h.name} {h.date:dd/MM}")) : "");

            bool hasDetails = !string.IsNullOrWhiteSpace(CommentsTextBox.Text);
            if (days.Count > 0 && !hasDetails)
                PreviewDetailText.Text += "\nAdd some details for your approver to send the request.";
            SubmitButton.IsEnabled = days.Count > 0 && hasDetails && _leaveService != null;
        }

        private async void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            var days = CalculateDays(out var error);
            if (days == null || days.Count == 0 || string.IsNullOrWhiteSpace(CommentsTextBox.Text))
                return;
            TryGetTimes(out var start, out var end, out error);

            var type = SelectedType;
            var from = StartDatePicker.SelectedDate.Value;
            var to = EndDatePicker.SelectedDate.Value;
            decimal dayCount = LeaveDayCalculator.DaysFor(days, _pattern);
            string when = from == to ? $"{from:ddd dd MMM yyyy}" : $"{from:ddd dd MMM} to {to:ddd dd MMM yyyy}";
            string times = start.HasValue || end.HasValue ? $" ({start?.ToString(@"hh\:mm") ?? "start"}–{end?.ToString(@"hh\:mm") ?? "end"})" : "";
            string confirm = $"{type.Name}: {when}{times}\n{dayCount:0.##} day{(dayCount == 1 ? "" : "s")}"
                + (PreviewBalanceText.Text.Length > 0 ? $"\n{PreviewBalanceText.Text}" : "")
                + "\n\nSend this request for approval?";
            if (MessageBox.Show(confirm, "Confirm request", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var request = new LeaveRequest
            {
                EmployeeId = EmployeeId,
                LeaveTypeId = type.Id,
                StartDate = from,
                EndDate = to,
                StartTime = start?.ToString(@"hh\:mm"),
                EndTime = end?.ToString(@"hh\:mm"),
                RequesterComments = CommentsTextBox.Text.Trim()
            };

            SubmitButton.IsEnabled = false;
            try
            {
                await Task.Run(() => _leaveService.SubmitRequest(request));
                CommentsTextBox.Clear();
                MessageBox.Show("Your request has been sent for approval.", "Request sent", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadAsync();
            }
            catch (FaultException<OrganizationServiceFault> fault)
            {
                // Plug-in validation (e.g. overlapping leave) comes back as a fault with a readable message
                MessageBox.Show(fault.Detail.Message, "Request not sent", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not send the request: {ex.Message}", "Leave", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdatePreview();
            }
        }

        // ---------- My requests (E10, E11) ----------

        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            if (RequestsGrid == null || TypeFilter == null)
                return;

            IEnumerable<LeaveRequest> rows = _requests;
            switch (WhenFilter.SelectedIndex)
            {
                case 0: rows = rows.Where(r => r.IsUpcoming).OrderBy(r => r.StartDate); break;
                case 1: rows = rows.Where(r => !r.IsUpcoming).OrderByDescending(r => r.StartDate); break;
                default: rows = rows.OrderByDescending(r => r.StartDate); break;
            }
            if ((StatusFilter.SelectedItem as ComboBoxItem)?.Tag is string status
                && Enum.TryParse(status, out LeaveRequestStatus wanted))
                rows = rows.Where(r => r.Status == wanted);
            if (TypeFilter.SelectedItem is string typeName && typeName != AnyType)
                rows = rows.Where(r => r.LeaveTypeName == typeName);

            RequestsGrid.ItemsSource = rows.ToList();
            ShowRequestDetail(null);
        }

        private void RequestsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var request = RequestsGrid.SelectedItem as LeaveRequest;
            CancelRequestButton.IsEnabled = request?.CanCancel == true;
            ShowRequestDetail(request);
        }

        private void ShowRequestDetail(LeaveRequest r)
        {
            if (r == null)
            {
                RequestDetailPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var lines = new List<string> { $"{r.LeaveTypeName} · {r.DatesText} · {r.Days:0.##} day{(r.Days == 1 ? "" : "s")} ({r.Hours:0.##}h) · {r.StatusText}" };
            if (r.RequestedOn.HasValue) lines.Add($"Requested {r.RequestedOn:dd/MM/yyyy HH:mm}");
            if (!string.IsNullOrEmpty(r.ApproverName))
                lines.Add($"Approver: {r.ApproverName}" + (string.IsNullOrEmpty(r.AdditionalApproverName) ? "" : $" (or {r.AdditionalApproverName})"));
            if (!string.IsNullOrWhiteSpace(r.RequesterComments)) lines.Add($"Your details: {r.RequesterComments}");
            if (!string.IsNullOrWhiteSpace(r.ApproverComments)) lines.Add($"Approver's comments: {r.ApproverComments}");
            if (r.CancellationRequestedOn.HasValue) lines.Add($"Cancellation requested {r.CancellationRequestedOn:dd/MM/yyyy HH:mm}");
            if (!string.IsNullOrWhiteSpace(r.CancellationComments)) lines.Add($"Cancellation reason: {r.CancellationComments}");

            RequestDetailText.Text = string.Join("\n", lines);
            RequestDetailPanel.Visibility = Visibility.Visible;
        }

        private async void CancelRequestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(RequestsGrid.SelectedItem is LeaveRequest request) || !request.CanCancel)
                return;

            // Pending is withdrawn straight away; approved leave goes to the approver to confirm
            string comment;
            if (request.CancelsInstantly)
            {
                comment = InputDialog.ShowDialog($"Cancel your request for {request.DatesText}?\n\nReason (optional):", "Cancel request");
                if (comment == null)
                    return;
            }
            else
            {
                comment = InputDialog.ShowDialog(
                    $"{request.ApproverName} will be asked to confirm cancelling {request.DatesText}. "
                    + "It stays booked (and on your timesheet) until they do.\n\nReason for cancelling:", "Ask to cancel");
                if (comment == null)
                    return;
                if (string.IsNullOrWhiteSpace(comment))
                {
                    MessageBox.Show("Please give your approver a reason.", "Ask to cancel", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            try
            {
                var status = await Task.Run(() => _leaveService.CancelRequest(request, comment));
                MessageBox.Show(status == LeaveRequestStatus.Cancelled
                        ? "Your request has been cancelled."
                        : $"{request.ApproverName} has been asked to confirm the cancellation.",
                    "Leave", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadAsync();
            }
            catch (FaultException<OrganizationServiceFault> fault)
            {
                MessageBox.Show(fault.Detail.Message, "Not cancelled", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not cancel: {ex.Message}", "Leave", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        // ---------- Company holidays (E12) ----------

        private void Holidays_Changed(object sender, RoutedEventArgs e) => ShowHolidays();

        private void ShowHolidays()
        {
            if (HolidaysList == null)
                return;
            bool past = ShowPastHolidaysCheckBox.IsChecked == true;
            HolidaysList.ItemsSource = _companyHolidays
                .Where(h => past || h.date >= DateTime.Today)
                .Select(h => $"{h.date:ddd dd MMM yyyy}   {h.name}")
                .ToList();
        }
    }
}
