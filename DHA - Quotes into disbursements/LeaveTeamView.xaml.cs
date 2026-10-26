using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xrm.Sdk;

namespace DHA.DSTC.WPF
{
    /// <summary>
    /// "My team" (line managers, additional approvers) or "Everyone" (HR) sub-tab of Leave - parity plan M4-M6:
    /// a person's balance for last, this and next year, their requests, and cancelling on their behalf.
    /// Balances come from the fwp_GetLeaveBalance Custom API because managers can't read entitlements directly.
    /// </summary>
    public partial class LeaveTeamView : UserControl
    {
        private const string AnyType = "Any type";
        private LeaveService _leaveService;
        private ApproverScope _scope;
        private List<LeavePerson> _people = new List<LeavePerson>();
        private List<LeaveRequest> _requests = new List<LeaveRequest>();
        private bool _peopleLoaded;

        public LeaveTeamView()
        {
            InitializeComponent();
        }

        /// <summary>Set by LeaveTabView each time the Leave tab is shown.</summary>
        public ApproverScope Scope
        {
            get => _scope;
            set
            {
                _scope = value;
                _peopleLoaded = false;
                if (IsVisible) _ = LoadPeopleAsync();
            }
        }

        private LeavePerson SelectedPerson => PeopleList.SelectedItem as LeavePerson;

        private async void TeamView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible && !_peopleLoaded)
                await LoadPeopleAsync();
        }

        private async Task LoadPeopleAsync()
        {
            if (_scope == null || !_scope.CanApprove || ServiceLocator.DataverseConnector == null)
                return;
            _leaveService = _leaveService ?? new LeaveService(ServiceLocator.DataverseConnector);
            _peopleLoaded = true;
            try
            {
                var scope = _scope;
                var result = await Task.Run(() => new
                {
                    People = scope.IsLeaveAdmin ? _leaveService.GetEveryone() : scope.MyPeople,
                    Types = _leaveService.GetLeaveTypes()
                });
                _people = result.People;
                PeopleTitleText.Text = scope.IsLeaveAdmin ? $"Everyone ({_people.Count})" : $"My people ({_people.Count})";

                var typeFilter = TypeFilter.SelectedItem as string;
                TypeFilter.ItemsSource = new[] { AnyType }.Concat(result.Types.Select(t => t.Name)).ToList();
                TypeFilter.SelectedItem = typeFilter != null && result.Types.Any(t => t.Name == typeFilter) ? typeFilter : AnyType;

                ShowPeople();
            }
            catch (Exception ex)
            {
                _peopleLoaded = false;
                MessageBox.Show($"Could not load your team: {ex.Message}", "Leave", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Search_Changed(object sender, TextChangedEventArgs e) => ShowPeople();

        private void ShowPeople()
        {
            var selected = SelectedPerson?.Id;
            var search = (SearchTextBox.Text ?? "").Trim();
            var rows = _people.Where(p => search.Length == 0 || (p.Name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            PeopleList.ItemsSource = rows;
            PeopleList.SelectedItem = rows.FirstOrDefault(p => p.Id == selected);
        }

        private async void PeopleList_SelectionChanged(object sender, SelectionChangedEventArgs e) => await LoadPersonAsync();

        private async Task LoadPersonAsync()
        {
            var person = SelectedPerson;
            if (person == null || _leaveService == null)
                return;

            PersonTitleText.Text = person.Name;
            BalanceGrid.ItemsSource = null;
            RequestsGrid.ItemsSource = null;
            ShowRequestDetail(null);
            try
            {
                var year = DateTime.Today.Year;
                var result = await Task.Run(() => new
                {
                    Balances = new[] { year - 1, year, year + 1 }.Select(y => new { Year = y, Balance = _leaveService.GetBalance(person.Id, y) }).ToList(),
                    Requests = _leaveService.GetMyRequests(person.Id, new DateTime(year - 1, 1, 1))
                });
                if (SelectedPerson?.Id != person.Id)
                    return; // moved on

                string D(decimal d) => $"{d:0.##}";
                BalanceGrid.ItemsSource = result.Balances.Select(x => x.Balance.HasEntitlement
                    ? new
                    {
                        x.Year,
                        Entitlement = D(x.Balance.EntitlementDays) + Breakdown(x.Balance),
                        Taken = D(x.Balance.TakenDays),
                        Booked = D(x.Balance.BookedDays) + (x.Balance.PendingDays > 0 ? $" ({D(x.Balance.PendingDays)} pending)" : ""),
                        Remaining = D(x.Balance.RemainingDays)
                    }
                    : new { x.Year, Entitlement = "not set up", Taken = "", Booked = "", Remaining = "" }).ToList();

                _requests = result.Requests;
                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load {person.Name}'s leave: {ex.Message}", "Leave", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string Breakdown(LeaveBalance b)
        {
            var parts = new List<string>();
            if (b.CarriedForwardDays != 0) parts.Add($"{b.CarriedForwardDays:0.##} carried forward");
            if (b.AdjustmentDays != 0) parts.Add($"{b.AdjustmentDays:0.##} adjustment");
            if (b.CarriedToNextYearDays != 0) parts.Add($"{b.CarriedToNextYearDays:0.##} carried to next year");
            return parts.Count == 0 ? "" : $" ({b.BaseDays:0.##} base, " + string.Join(", ", parts) + ")";
        }

        // ---------- Requests (M5) ----------

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
            if ((StatusFilter.SelectedItem as ComboBoxItem)?.Tag is string status && Enum.TryParse(status, out LeaveRequestStatus wanted))
                rows = rows.Where(r => r.Status == wanted);
            if (TypeFilter.SelectedItem is string typeName && typeName != AnyType)
                rows = rows.Where(r => r.LeaveTypeName == typeName);

            RequestsGrid.ItemsSource = rows.ToList();
            ShowRequestDetail(null);
        }

        private void RequestsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var request = RequestsGrid.SelectedItem as LeaveRequest;
            CancelOnBehalfButton.IsEnabled = request != null && (request.CanCancel || request.IsCancellationRequest);
            ShowRequestDetail(request);
        }

        private void ShowRequestDetail(LeaveRequest r)
        {
            if (r == null)
            {
                RequestDetailPanel.Visibility = Visibility.Collapsed;
                CancelOnBehalfButton.IsEnabled = false;
                return;
            }

            var lines = new List<string> { $"{r.LeaveTypeName} · {r.DatesText} · {r.Days:0.##} day{(r.Days == 1 ? "" : "s")} ({r.Hours:0.##}h) · {r.StatusText}" };
            if (r.RequestedOn.HasValue) lines.Add($"Requested {r.RequestedOn:dd/MM/yyyy HH:mm}");
            if (!string.IsNullOrEmpty(r.ApproverName))
                lines.Add($"Approver: {r.ApproverName}" + (string.IsNullOrEmpty(r.AdditionalApproverName) ? "" : $" (or {r.AdditionalApproverName})"));
            if (!string.IsNullOrWhiteSpace(r.RequesterComments)) lines.Add($"Their details: {r.RequesterComments}");
            if (!string.IsNullOrWhiteSpace(r.ApproverComments)) lines.Add($"Approver's comments: {r.ApproverComments}");
            if (r.CancellationRequestedOn.HasValue) lines.Add($"Cancellation requested {r.CancellationRequestedOn:dd/MM/yyyy HH:mm}");
            if (!string.IsNullOrWhiteSpace(r.CancellationComments)) lines.Add($"Cancellation reason: {r.CancellationComments}");

            RequestDetailText.Text = string.Join("\n", lines);
            RequestDetailPanel.Visibility = Visibility.Visible;
        }

        // ---------- Cancel on behalf (M6) ----------

        private async void CancelOnBehalfButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(RequestsGrid.SelectedItem is LeaveRequest request) || SelectedPerson == null)
                return;

            var comment = InputDialog.ShowDialog(
                $"Cancel {SelectedPerson.Name}'s {request.LeaveTypeName} on {request.DatesText}?\n\n"
                + "It's cancelled straight away and they're emailed.\n\nReason (sent to them):",
                "Cancel on their behalf");
            if (comment == null)
                return;
            if (string.IsNullOrWhiteSpace(comment))
            {
                MessageBox.Show("Please give a reason - it's sent to them.", "Cancel on their behalf", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var me = ServiceLocator.CurrentUserId;
                await Task.Run(() => _leaveService.Decide(request, LeaveRequestStatus.Cancelled, comment, me));
                await LoadPersonAsync();
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

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadPersonAsync();
    }
}
