using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;
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
        }

        // Re-checked each time the tab is shown, so switching environment or user is picked up.
        // Uses the real signed-in user, not an impersonated one - approving is the approver's own action.
        private async void LeaveTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible || ServiceLocator.DataverseConnector == null)
                return;

            var userId = ServiceLocator.CurrentUserId;
            var email = ServiceLocator.CurrentUserEmail;
            bool canApprove = await Task.Run(() => new LeaveService(ServiceLocator.DataverseConnector).CanApprove(userId, email));

            ApprovalsTabItem.Visibility = canApprove ? Visibility.Visible : Visibility.Collapsed;
            if (!canApprove && LeaveSubTabs.SelectedItem == ApprovalsTabItem)
                LeaveSubTabs.SelectedIndex = 0;
        }
    }
}
