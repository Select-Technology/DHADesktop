using System;
using System.Linq;
using DHA.Leave.Plugins;
using Xunit;

namespace DHA.Leave.Plugins.Tests
{
    /// <summary>Entitlement is counted in days (HR decision 15 Oct 2026).</summary>
    public class LeaveDaysTests
    {
        private static readonly WorkingDay[] Pattern =
            LeaveDayCalculator.BuildPattern(LeaveDayCalculator.StandardLeaveDayHours, new decimal?[] { 8m, 8m, 8m, 8m, 7.5m });

        private static decimal Days(DateTime start, DateTime end, TimeSpan? st = null, TimeSpan? et = null, bool sh = false, bool eh = false) =>
            LeaveDayCalculator.DaysFor(LeaveDayCalculator.Calculate(start, end, st, et, Pattern, null, sh, eh), Pattern);

        [Fact]
        public void FullWeek_IsFiveDays() => Assert.Equal(5m, Days(new DateTime(2027, 3, 1), new DateTime(2027, 3, 7)));

        [Fact]
        public void HalfDays_AreHalf()
        {
            Assert.Equal(0.5m, Days(new DateTime(2027, 3, 1), new DateTime(2027, 3, 1), sh: true));
            Assert.Equal(0.5m, Days(new DateTime(2027, 3, 5), new DateTime(2027, 3, 5), eh: true));        // Friday, 4 of 8 hours
            Assert.Equal(2m, Days(new DateTime(2027, 3, 3), new DateTime(2027, 3, 5), sh: true, eh: true)); // 0.5 + 1 + 0.5
        }

        [Fact]
        public void Appointment_IsAShareOfTheDay()
        {
            // 13:45-14:45 on an 8.5h Monday
            Assert.Equal(0.12m, Days(new DateTime(2027, 3, 1), new DateTime(2027, 3, 1), new TimeSpan(13, 45, 0), new TimeSpan(14, 45, 0)));
        }

        [Fact]
        public void PartTimer_CountsOnlyTheirDays()
        {
            var pt = LeaveDayCalculator.BuildPattern(LeaveDayCalculator.StandardLeaveDayHours, new decimal?[] { 0m, 8m, 8m, 8m, 0m });
            var d = LeaveDayCalculator.Calculate(new DateTime(2027, 3, 1), new DateTime(2027, 3, 5), null, null, pt);
            Assert.Equal(3m, LeaveDayCalculator.DaysFor(d, pt));
        }

        [Theory]
        [InlineData(2026, 12, 29, 2027, 1, 4, true)]
        [InlineData(2027, 12, 20, 2027, 12, 31, false)]
        [InlineData(2027, 1, 1, 2027, 1, 1, false)]
        public void LeaveYearIsTheCalendarYear(int y1, int m1, int d1, int y2, int m2, int d2, bool spans)
        {
            Assert.Equal(spans, LeaveDayCalculator.SpansLeaveYears(new DateTime(y1, m1, d1), new DateTime(y2, m2, d2)));
        }
    }
}
