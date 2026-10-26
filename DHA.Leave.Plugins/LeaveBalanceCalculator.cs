using System;
using System.Collections.Generic;

namespace DHA.Leave.Plugins
{
    /// <summary>One request as far as the balance is concerned.</summary>
    public class BalanceRequest
    {
        public int Status { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Days { get; set; }
        /// <summary>The type uses entitlement (requests with no type count, as before types existed).</summary>
        public bool Deducts { get; set; } = true;
    }

    /// <summary>A person's leave for one leave (calendar) year, in days.</summary>
    public class LeaveBalanceFigures
    {
        public bool HasEntitlement { get; set; }
        public decimal BaseDays { get; set; }
        public decimal CarriedForwardDays { get; set; }
        public decimal AdjustmentDays { get; set; }
        public decimal CarriedToNextYearDays { get; set; }
        public decimal TakenDays { get; set; }
        /// <summary>Approved but not yet taken, plus pending.</summary>
        public decimal BookedDays { get; set; }
        public decimal PendingDays { get; set; }
        public decimal EntitlementDays => BaseDays + CarriedForwardDays + AdjustmentDays;
        public decimal RemainingDays => EntitlementDays - TakenDays - BookedDays;
    }

    /// <summary>
    /// The balance rules (parity plan E9, HR decisions 15 Oct 2026): leave years are calendar years, a request belongs to the
    /// year it starts in (it can't span two), only types that deduct count, and pending requests count against the balance.
    /// </summary>
    public static class LeaveBalanceCalculator
    {
        public static LeaveBalanceFigures Calculate(int year, decimal? baseDays, decimal? carriedForward, decimal? adjustment,
            decimal? carriedToNextYear, bool hasEntitlement, IEnumerable<BalanceRequest> requests, DateTime today)
        {
            var b = new LeaveBalanceFigures { HasEntitlement = hasEntitlement };
            if (!hasEntitlement)
                return b;

            b.BaseDays = baseDays ?? 0m;
            b.CarriedForwardDays = carriedForward ?? 0m;
            b.AdjustmentDays = adjustment ?? 0m;
            b.CarriedToNextYearDays = carriedToNextYear ?? 0m;

            foreach (var r in requests)
            {
                if (r.StartDate.Year != year || !r.Deducts)
                    continue;
                if (r.Status == LeaveStatus.Approved || r.Status == LeaveStatus.CancellationRequested)
                {
                    if (r.EndDate.Date < today.Date) b.TakenDays += r.Days;
                    else b.BookedDays += r.Days;
                }
                else if (r.Status == LeaveStatus.Pending)
                {
                    b.BookedDays += r.Days;
                    b.PendingDays += r.Days;
                }
            }
            return b;
        }
    }
}
