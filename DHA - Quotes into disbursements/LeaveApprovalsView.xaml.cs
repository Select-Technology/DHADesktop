using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;
using DHA.Leave.Plugins;
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
    /// "Approvals" sub-tab of Leave: HR approve or reject requests and cancellations (DHA-9 phase B).
    /// Only shown to approvers - see LeaveService.CanApprove.
    /// </summary>
    public partial class LeaveApprovalsView : UserControl
    {
        private LeaveService _leaveService;
        private List<LeaveType> _types = new List<LeaveType>();
        private List<LeaveRequest> _upcoming = new List<LeaveRequest>();
        private bool _loading;

        public LeaveApprovalsView()
        {
            InitializeComponent();
        }

        private async void ApprovalsView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible || _loading || ServiceLocator.DataverseConnector == null)
                return;
            _leaveService = _leaveService ?? new LeaveService(ServiceLocator.DataverseConnector);
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            _loading = true;
            var selectedId = (PendingGrid.SelectedItem as LeaveRequest)?.Id;
            try
            {
                var today = DateTime.Today;
                var result = await Task.Run(() => new
                {
                    Pending = _leaveService.GetRequestsAwaitingDecision(),
                    Upcoming = _leaveService.GetApprovedLeave(today, today.AddDays(30)),
                    Types = _leaveService.GetLeaveTypes()
                });

                _types = result.Types;
                _upcoming = result.Upcoming;
                PendingGrid.ItemsSource = result.Pending;
                UpcomingGrid.ItemsSource = result.Upcoming;
                PendingTitleText.Text = result.Pending.Count == 0
                    ? "Waiting for a decision - nothing to do"
                    : $"Waiting for a decision ({result.Pending.Count})";

                PendingGrid.SelectedItem = result.Pending.FirstOrDefault(r => r.Id == selectedId);
                if (PendingGrid.SelectedItem == null)
                    ShowDetail(null);
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

        private async void PendingGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var request = PendingGrid.SelectedItem as LeaveRequest;
            ShowDetail(request);
            if (request != null)
                await ShowBalanceAsync(request);
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
                DetailKindText.Text = "Wants to cancel approved leave";
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

            DetailDatesText.Text = request.DatesText;
            DetailTypeText.Text = string.IsNullOrEmpty(request.LeaveTypeName) ? "(not given)" : request.LeaveTypeName;
            DetailHoursText.Text = $"{request.Hours:0.##} hours";
            DetailRequestedText.Text = request.RequestedOn?.ToString("ddd dd/MM/yyyy HH:mm") ?? "";
            DetailCommentsText.Text = string.IsNullOrWhiteSpace(request.RequesterComments) ? "(none)" : request.RequesterComments;
            DetailBalanceText.Text = "Loading…";
            ApproverCommentTextBox.Text = "";

            // Anyone else off at the same time
            var others = _upcoming.Where(o => o.EmployeeId != request.EmployeeId
                                              && o.StartDate <= request.EndDate && o.EndDate >= request.StartDate)
                                  .Select(o => o.EmployeeName).Distinct().ToList();
            DetailOverlapText.Text = others.Count > 0 ? "Also off during these dates: " + string.Join(", ", others) : "";
            DetailOverlapText.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task ShowBalanceAsync(LeaveRequest request)
        {
            try
            {
                var today = DateTime.Today;
                var balanceInfo = await Task.Run(() =>
                {
                    var requests = _leaveService.GetMyRequests(request.EmployeeId, today.AddYears(-1));
                    var pattern = _leaveService.GetWorkingPattern(request.EmployeeId);
                    return new { Balance = _leaveService.GetBalance(request.EmployeeId, request.StartDate, requests, _types), Pattern = pattern };
                });

                if (!ReferenceEquals(PendingGrid.SelectedItem, request))
                    return; // moved on

                var b = balanceInfo.Balance;
                if (b.YearStart == default(DateTime))
                {
                    DetailBalanceText.Text = "No entitlement set up for this leave year.";
                    return;
                }

                decimal day = balanceInfo.Pattern.Where(d => d.ExpectedHours > 0).Select(d => d.ExpectedHours)
                    .DefaultIfEmpty(LeaveDayCalculator.DefaultDayHours).Max();
                string Days(decimal h) => $"{h / day:0.##} days";

                var type = _types.FirstOrDefault(t => t.Id == request.LeaveTypeId);
                bool deducts = type == null || type.DeductsFromEntitlement;
                decimal afterThis = b.RemainingHours - (deducts && !request.IsCancellationRequest ? request.Hours : 0m);

                DetailBalanceText.Text = $"{Days(b.RemainingHours)} remaining of {Days(b.EntitlementHours)}"
                    + (deducts && !request.IsCancellationRequest ? $" - {Days(afterThis)} if approved" : "")
                    + (deducts ? "" : $" - {type.Name} doesn't use entitlement");
                DetailBalanceText.Foreground = afterThis < 0
                    ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
                    : (Brush)FindResource("TextPrimaryBrush");
            }
            catch (Exception ex)
            {
                DetailBalanceText.Text = "Couldn't load balance: " + ex.Message;
            }
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
