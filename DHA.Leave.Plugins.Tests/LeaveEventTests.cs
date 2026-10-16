using DHA.Leave.Plugins;
using Xunit;

namespace DHA.Leave.Plugins.Tests
{
    public class LeaveEventTests
    {
        [Theory]
        [InlineData(LeaveStatus.Pending, LeaveStatus.Approved, LeaveEvent.Approved)]
        [InlineData(LeaveStatus.Pending, LeaveStatus.Rejected, LeaveEvent.Rejected)]
        [InlineData(LeaveStatus.Pending, LeaveStatus.Cancelled, LeaveEvent.Withdrawn)]
        [InlineData(LeaveStatus.Approved, LeaveStatus.CancellationRequested, LeaveEvent.CancellationRequested)]
        [InlineData(LeaveStatus.CancellationRequested, LeaveStatus.Cancelled, LeaveEvent.CancellationApproved)]
        [InlineData(LeaveStatus.CancellationRequested, LeaveStatus.Approved, LeaveEvent.CancellationDeclined)]
        [InlineData(LeaveStatus.Approved, LeaveStatus.Cancelled, LeaveEvent.CancelledByHR)]
        public void StatusChange_MapsToEvent(int oldStatus, int newStatus, string expected)
        {
            Assert.Equal(expected, LeaveEvent.For(oldStatus, newStatus));
        }

        [Fact]
        public void PendingCancelledByTheEmployeeIsWithdrawn_ByAnApproverIsCancelled()
        {
            Assert.Equal(LeaveEvent.Withdrawn, LeaveEvent.For(LeaveStatus.Pending, LeaveStatus.Cancelled, byEmployee: true));
            Assert.Equal(LeaveEvent.CancelledByHR, LeaveEvent.For(LeaveStatus.Pending, LeaveStatus.Cancelled, byEmployee: false));
            Assert.Equal(LeaveEvent.CancelledByHR, LeaveEvent.For(LeaveStatus.Approved, LeaveStatus.Cancelled, byEmployee: false));
        }

        [Theory]
        [InlineData(LeaveStatus.Approved, true)]
        [InlineData(LeaveStatus.CancellationRequested, true)]
        [InlineData(LeaveStatus.Pending, false)]
        [InlineData(LeaveStatus.Rejected, false)]
        [InlineData(LeaveStatus.Cancelled, false)]
        public void OnlyApprovedLeaveGoesToTheApproverForCancellation(int oldStatus, bool allowed)
        {
            Assert.Equal(allowed, LeaveEvent.CanRequestCancellation(oldStatus));
        }
    }
}
