using System;
using DHA.Leave.Plugins;
using Xunit;

namespace DHA.Leave.Plugins.Tests
{
    public class LeaveYearEndTests
    {
        // Run on 1 January 2027, closing 2026
        private static readonly DateTime AfterYearEnd = new DateTime(2027, 1, 1);

        private static BalanceRequest R(int status, string start, decimal days, bool deducts = true) => new BalanceRequest
        {
            Status = status, StartDate = DateTime.Parse(start), EndDate = DateTime.Parse(start), Days = days, Deducts = deducts
        };

        private static decimal Carry(decimal entitlement, decimal? overrideLimit, params BalanceRequest[] requests) =>
            LeaveYearEnd.CarryForwardDays(
                LeaveBalanceCalculator.Calculate(2026, entitlement, null, null, null, true, requests, AfterYearEnd),
                LeaveYearEnd.StandardCarryForwardLimit, overrideLimit);

        [Fact]
        public void UnusedDaysUnderTheLimitAllCarry()
        {
            Assert.Equal(3m, Carry(25m, null, R(LeaveStatus.Approved, "2026-08-03", 22m)));
        }

        [Fact]
        public void CarryIsCappedAtFiveDays()
        {
            Assert.Equal(5m, Carry(25m, null, R(LeaveStatus.Approved, "2026-08-03", 12m)));
        }

        [Fact]
        public void HrOverrideReplacesTheLimit_UpOrDown()
        {
            Assert.Equal(8m, Carry(25m, 10m, R(LeaveStatus.Approved, "2026-08-03", 17m)));
            Assert.Equal(2m, Carry(25m, 2m, R(LeaveStatus.Approved, "2026-08-03", 17m)));
            Assert.Equal(0m, Carry(25m, 0m, R(LeaveStatus.Approved, "2026-08-03", 17m)));
        }

        [Fact]
        public void PendingCountsAsTaken()
        {
            Assert.Equal(1m, Carry(25m, null, R(LeaveStatus.Approved, "2026-08-03", 20m), R(LeaveStatus.Pending, "2026-12-30", 4m)));
        }

        [Fact]
        public void OverdrawnBalanceCarriesNothing()
        {
            Assert.Equal(0m, Carry(20m, null, R(LeaveStatus.Approved, "2026-08-03", 22m)));
        }

        [Fact]
        public void HalfDaysAndPartDaysCarry()
        {
            Assert.Equal(2.5m, Carry(25m, null, R(LeaveStatus.Approved, "2026-08-03", 22.5m)));
            Assert.Equal(4.88m, Carry(25m, null, R(LeaveStatus.Approved, "2026-08-03", 20m), R(LeaveStatus.Approved, "2026-09-01", 0.12m)));
        }

        [Fact]
        public void NonDeductingCancelledAndNextYearsLeaveDontReduceTheCarry()
        {
            Assert.Equal(5m, Carry(25m, null,
                R(LeaveStatus.Approved, "2026-08-03", 20m),
                R(LeaveStatus.Approved, "2026-08-10", 5m, deducts: false),
                R(LeaveStatus.Cancelled, "2026-09-01", 3m),
                R(LeaveStatus.Approved, "2027-01-04", 2m)));
        }

        [Fact]
        public void PartTimerWithASmallerEntitlement()
        {
            // Carried forward and adjustment on the closing year count towards what's unused
            var balance = LeaveBalanceCalculator.Calculate(2026, 15m, 2m, -1m, null, true,
                new[] { R(LeaveStatus.Approved, "2026-05-04", 13m) }, AfterYearEnd);
            Assert.Equal(3m, LeaveYearEnd.CarryForwardDays(balance, 5m, null));
        }

        [Fact]
        public void NoEntitlementCarriesNothing()
        {
            var balance = LeaveBalanceCalculator.Calculate(2026, null, null, null, null, false, new BalanceRequest[0], AfterYearEnd);
            Assert.Equal(0m, LeaveYearEnd.CarryForwardDays(balance, 5m, null));
        }
    }
}
