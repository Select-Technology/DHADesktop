using System;
using DHA.Leave.Plugins;
using Xunit;

namespace DHA.Leave.Plugins.Tests
{
    public class LeaveBalanceTests
    {
        private static readonly DateTime Today = new DateTime(2027, 6, 15);

        private static BalanceRequest R(int status, string start, string end, decimal days, bool deducts = true) => new BalanceRequest
        {
            Status = status, StartDate = DateTime.Parse(start), EndDate = DateTime.Parse(end), Days = days, Deducts = deducts
        };

        private static LeaveBalanceFigures Calc(params BalanceRequest[] requests) =>
            LeaveBalanceCalculator.Calculate(2027, 25m, 3m, -1m, 2m, true, requests, Today);

        [Fact]
        public void EntitlementIsBasePlusCarriedForwardPlusAdjustment()
        {
            var b = Calc();
            Assert.Equal(27m, b.EntitlementDays);
            Assert.Equal(27m, b.RemainingDays);
            Assert.Equal(2m, b.CarriedToNextYearDays);
        }

        [Fact]
        public void PastApprovedIsTaken_FutureApprovedAndPendingAreBooked()
        {
            var b = Calc(R(LeaveStatus.Approved, "2027-03-01", "2027-03-05", 5m),
                         R(LeaveStatus.CancellationRequested, "2027-04-01", "2027-04-01", 1m),
                         R(LeaveStatus.Approved, "2027-08-02", "2027-08-06", 5m),
                         R(LeaveStatus.Pending, "2027-09-01", "2027-09-01", 0.5m));
            Assert.Equal(6m, b.TakenDays);
            Assert.Equal(5.5m, b.BookedDays);
            Assert.Equal(0.5m, b.PendingDays);
            Assert.Equal(15.5m, b.RemainingDays);
        }

        [Fact]
        public void LeaveThatEndsTodayIsStillBooked()
        {
            Assert.Equal(1m, Calc(R(LeaveStatus.Approved, "2027-06-15", "2027-06-15", 1m)).BookedDays);
        }

        [Fact]
        public void RejectedCancelledNonDeductingAndOtherYearsDontCount()
        {
            var b = Calc(R(LeaveStatus.Rejected, "2027-03-01", "2027-03-01", 1m),
                         R(LeaveStatus.Cancelled, "2027-03-02", "2027-03-02", 1m),
                         R(LeaveStatus.Approved, "2027-03-03", "2027-03-03", 1m, deducts: false),
                         R(LeaveStatus.Approved, "2026-12-30", "2026-12-31", 2m),
                         R(LeaveStatus.Pending, "2028-01-04", "2028-01-04", 1m));
            Assert.Equal(0m, b.TakenDays + b.BookedDays);
        }

        [Fact]
        public void NoEntitlementGivesAnEmptyBalance()
        {
            var b = LeaveBalanceCalculator.Calculate(2027, null, null, null, null, false,
                new[] { R(LeaveStatus.Approved, "2027-03-01", "2027-03-01", 1m) }, Today);
            Assert.False(b.HasEntitlement);
            Assert.Equal(0m, b.TakenDays);
        }
    }
}
