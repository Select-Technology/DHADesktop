using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DHA.DSTC.WPF
{
    public partial class LeaveTabView : UserControl
    {
        public LeaveTabView()
        {
            InitializeComponent();
            ApprovalsView.PendingCountChanged += ShowPendingCount;
        }

        // Re-checked each time the tab is shown, so switching environment or user, or HR changing approvers, is picked up.
        // Uses the real signed-in user, not an impersonated one - approving is the approver's own action.
        private async void LeaveTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible || ServiceLocator.DataverseConnector == null)
                return;

            var userId = ServiceLocator.CurrentUserId;
            var email = ServiceLocator.CurrentUserEmail;
            var service = new LeaveService(ServiceLocator.DataverseConnector);
            try
            {
                var result = await Task.Run(() =>
                {
                    var s = service.GetApproverScope(userId, email);
                    return new { Scope = s, Waiting = s.CanApprove ? service.GetRequestsAwaitingDecision(s).Count : 0 };
                });
                var scope = result.Scope;

                ApprovalsView.Scope = scope;
                TeamView.Scope = scope;
                var visibility = scope.CanApprove ? Visibility.Visible : Visibility.Collapsed;
                ApprovalsTabItem.Visibility = TeamTabItem.Visibility = visibility;
                TeamTabItem.Header = scope.IsLeaveAdmin ? "Everyone" : "My team";
                ShowPendingCount(result.Waiting);
                if (!scope.CanApprove && LeaveSubTabs.SelectedIndex > 0)
                    LeaveSubTabs.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LeaveTabView: {ex.Message}");
            }
        }

        // M2: count on the tab header
        private void ShowPendingCount(int waiting) =>
            ApprovalsTabItem.Header = waiting > 0 ? $"Approvals ({waiting})" : "Approvals";
    }
}
