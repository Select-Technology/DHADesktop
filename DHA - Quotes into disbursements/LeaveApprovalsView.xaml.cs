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
using System.Windows.Media;
using Microsoft.Xrm.Sdk;

namespace DHA.DSTC.WPF
{
    /// <summary>
    /// "Approvals" sub-tab of Leave: approve or reject requests and cancellations (DHA-9 phase B, parity plan M1-M3).
    /// HR see everyone's; line managers and additional approvers see their people's. Shown when <see cref="Scope"/> allows.
    /// </summary>
    public partial class LeaveApprovalsView : UserControl
    {
        private LeaveService _leaveService;
        private ApproverScope _scope;
        private List<LeaveType> _types = new List<LeaveType>();
        private List<LeaveRequest> _waiting = new List<LeaveRequest>();
        private List<LeaveRequest> _whosOff = new List<LeaveRequest>();
        private bool _loading;

        /// <summary>Raised after each load with the number of requests waiting (for the tab header).</summary>
        public event Action<int> PendingCountChanged;

        public LeaveApprovalsView()
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
                // "My people" only means something to someone who approves people directly
                WhoFilter.Visibility = value?.MyPeople.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (value?.MyPeople.Count == 0) WhoFilter.SelectedIndex = 0;
                if (IsVisible) _ = LoadAsync();
            }
        }

        private async void ApprovalsView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
                return;
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (_loading || _scope == null || !_scope.CanApprove || ServiceLocator.DataverseConnector == null)
                return;
            _leaveService = _leaveService ?? new LeaveService(ServiceLocator.DataverseConnector);
            _loading = true;
            var selectedId = (PendingGrid.SelectedItem as LeaveRequest)?.Id;
            try
            {
                var today = DateTime.Today;
                var scope = _scope;
                var result = await Task.Run(() => new
                {
                    Waiting = _leaveService.GetRequestsAwaitingDecision(scope),
                    WhosOff = _leaveService.GetTeamLeave(today, today.AddDays(30)),
                    Types = _leaveService.GetLeaveTypes()
                });

                _types = result.Types;
                _waiting = result.Waiting;
                _whosOff = result.WhosOff;
                ShowWaiting(selectedId);
                ShowWhosOff();
                PendingCountChanged?.Invoke(_waiting.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load leave requests: {ex.Message}", "Approvals", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _loading = false;
            }
        }

        // ---------- Lists (M2, M3) ----------

        private void PendingFilter_Changed(object sender, RoutedEventArgs e) => ShowWaiting((PendingGrid?.SelectedItem as LeaveRequest)?.Id);
        private void WhoFilter_Changed(object sender, SelectionChangedEventArgs e) => ShowWhosOff();

        private bool MyPeopleOnly => WhoFilter?.SelectedIndex == 1 && _scope != null;

        private void ShowWaiting(Guid? selectedId)
        {
            if (PendingGrid == null || ShowCancellationsCheckBox == null)
                return;
            var rows = _waiting.Where(r => r.IsCancellationRequest ? ShowCancellationsCheckBox.IsChecked == true : ShowNewCheckBox.IsChecked == true).ToList();
            PendingGrid.ItemsSource = rows;
            int requests = _waiting.Count(r => !r.IsCancellationRequest), cancellations = _waiting.Count - requests;
            PendingTitleText.Text = _waiting.Count == 0
                ? "Waiting for a decision - nothing to do"
                : $"Waiting for a decision ({requests} new, {cancellations} to cancel)";

            PendingGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selectedId);
            if (PendingGrid.SelectedItem == null)
                ShowDetail(null);
        }

        private void ShowWhosOff()
        {
            if (UpcomingGrid == null)
                return;
            UpcomingGrid.ItemsSource = _whosOff.Where(r => !MyPeopleOnly || _scope.IsMine(r.EmployeeId)).ToList();
        }

        // ---------- Decision panel ----------

        private async void PendingGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var request = PendingGrid.SelectedItem as LeaveRequest;
            ShowDetail(request);
            if (request != null)
                await ShowBalanceAndOverlapAsync(request);
        }

        private void ShowDetail(LeaveRequest request)
        {
            NothingSelectedText.Visibility = request == null ? Visibility.Visible : Visibility.Collapsed;
            DetailPanel.Visibility = request == null ? Visibility.Collapsed : Visibility.Visible;
            if (request == null)
                return;

            DetailTitleText.Text = request.EmployeeName;
            if (request.IsCancellationRequest)
            {
                DetailKindText.Text = "Wants to cancel approved leave"
                    + (string.IsNullOrWhiteSpace(request.CancellationComments) ? "" : $": \"{request.CancellationComments}\"");
                DetailKindText.Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
                ApproveButton.Content = "Approve cancellation";
                RejectButton.Content = "Keep leave booked";
            }
            else
            {
                DetailKindText.Text = "New leave request";
                DetailKindText.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
                ApproveButton.Content = "Approve";
                RejectButton.Content = "Reject";
            }
            DetailKindText.TextWrapping = TextWrapping.Wrap;

            DetailDatesText.Text = request.DatesText;
            DetailTypeText.Text = string.IsNullOrEmpty(request.LeaveTypeName) ? "(not given)" : request.LeaveTypeName;
            DetailHoursText.Text = $"{request.Days:0.##} day{(request.Days == 1 ? "" : "s")} ({request.Hours:0.##} hours)";
            DetailRequestedText.Text = (request.RequestedOn?.ToString("ddd dd/MM/yyyy HH:mm") ?? "")
                + (string.IsNullOrEmpty(request.ApproverName) ? "" : $" · approver {request.ApproverName}")
                + (string.IsNullOrEmpty(request.AdditionalApproverName) ? "" : $" or {request.AdditionalApproverName}");
            DetailCommentsText.Text = string.IsNullOrWhiteSpace(request.RequesterComments) ? "(none)" : request.RequesterComments;
            DetailBalanceText.Text = "Loading…";
            DetailOverlapText.Visibility = Visibility.Collapsed;
            ApproverCommentTextBox.Text = "";
        }

        private async Task ShowBalanceAndOverlapAsync(LeaveRequest request)
        {
            try
            {
                var info = await Task.Run(() => new
                {
                    Balance = _leaveService.GetBalance(request.EmployeeId, request.StartDate.Year),
                    Overlap = _leaveService.GetTeamLeave(request.StartDate, request.EndDate)
                });
                if (!ReferenceEquals(PendingGrid.SelectedItem, request))
                    return; // moved on

                ShowOverlap(request, info.Overlap);

                var b = info.Balance;
                if (!b.HasEntitlement)
                {
                    DetailBalanceText.Text = $"No entitlement set up for {request.StartDate.Year}.";
                    DetailBalanceText.Foreground = (Brush)FindResource("TextPrimaryBrush");
                    return;
                }

                string Days(decimal d) => $"{d:0.##} days";
                var type = _types.FirstOrDefault(t => t.Id == request.LeaveTypeId);
                bool deducts = type == null || type.DeductsFromEntitlement;
                // Pending requests (including this one) already count as taken, so "remaining" already reflects it
                decimal remaining = b.RemainingDays;
                decimal withoutThis = remaining + (deducts && !request.IsCancellationRequest ? request.Days : 0m);

                DetailBalanceText.Text = deducts
                    ? $"{Days(remaining)} remaining of {Days(b.EntitlementDays)} in {request.StartDate.Year} if approved ({Days(withoutThis)} before this request)"
                    : $"{Days(remaining)} remaining of {Days(b.EntitlementDays)} - {type.Name} doesn't use entitlement";
                DetailBalanceText.Foreground = remaining < 0
                    ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
                    : (Brush)FindResource("TextPrimaryBrush");
            }
            catch (Exception ex)
            {
                DetailBalanceText.Text = "Couldn't load balance: " + ex.Message;
            }
        }

        /// <summary>Anyone else off (or asking to be) during the request, honouring the Everyone / My people filter.</summary>
        private void ShowOverlap(LeaveRequest request, List<LeaveRequest> overlap)
        {
            var others = overlap.Where(o => o.EmployeeId != request.EmployeeId && (!MyPeopleOnly || _scope.IsMine(o.EmployeeId)))
                .GroupBy(o => o.EmployeeName)
                .Select(g => g.Key + (g.All(o => o.Status == LeaveRequestStatus.Pending) ? " (pending)" : ""))
                .ToList();
            DetailOverlapText.Text = others.Count > 0 ? "Also off during these dates: " + string.Join(", ", others) : "Nobody else is off during these dates.";
            DetailOverlapText.Visibility = Visibility.Visible;
        }

        private async void ApproveButton_Click(object sender, RoutedEventArgs e)
        {
            if (PendingGrid.SelectedItem is LeaveRequest request)
                await DecideAsync(request, request.IsCancellationRequest ? LeaveRequestStatus.Cancelled : LeaveRequestStatus.Approved);
        }

        private async void RejectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(PendingGrid.SelectedItem is LeaveRequest request))
                return;

            // Turning down a cancellation leaves the original approved leave in place
            var status = request.IsCancellationRequest ? LeaveRequestStatus.Approved : LeaveRequestStatus.Rejected;
            if (status == LeaveRequestStatus.Rejected && string.IsNullOrWhiteSpace(ApproverCommentTextBox.Text)
                && MessageBox.Show($"Reject {request.EmployeeName}'s leave without a comment?", "Reject leave",
                       MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await DecideAsync(request, status);
        }

        private async Task DecideAsync(LeaveRequest request, LeaveRequestStatus status)
        {
            ApproveButton.IsEnabled = RejectButton.IsEnabled = false;
            try
            {
                var comment = ApproverCommentTextBox.Text;
                var approverId = ServiceLocator.CurrentUserId;
                await Task.Run(() => _leaveService.Decide(request, status, comment, approverId));
                PendingGrid.SelectedItem = null;
                await LoadAsync();
            }
            catch (FaultException<OrganizationServiceFault> fault)
            {
                MessageBox.Show(fault.Detail.Message, "Leave not updated", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not update the request: {ex.Message}", "Approvals", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ApproveButton.IsEnabled = RejectButton.IsEnabled = true;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    }
}
