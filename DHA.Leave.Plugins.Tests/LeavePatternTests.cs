using System;
using System.Linq;
using DHA.Leave.Plugins;
using Xunit;

namespace DHA.Leave.Plugins.Tests
{
    /// <summary>HR decision 15 Oct 2026: a day of leave is 8.5h Mon-Thu, 8h Fri; part-timers work specific days.</summary>
    public class LeavePatternTests
    {
        private static readonly decimal[] Standard = LeaveDayCalculator.StandardLeaveDayHours;

        [Fact]
        public void FullTimer_LeaveDayHoursComeFromTheSetting_NotColleagueConfiguration()
        {
            // prod-style colleague configuration: 8h Mon-Thu, 7.5h Fri expected
            var p = LeaveDayCalculator.BuildPattern(Standard, new decimal?[] { 8m, 8m, 8m, 8m, 7.5m },
                new[] { "08:30", "08:30", "08:30", "08:30", "08:30" }, new[] { "17:30", "17:30", "17:30", "17:30", "17:00" });
            Assert.Equal(new[] { 8.5m, 8.5m, 8.5m, 8.5m, 8m, 0m, 0m }, p.Select(d => d.ExpectedHours).ToArray());
        }

        [Fact]
        public void FullWeekOfLeave_Is42Hours()
        {
            var p = LeaveDayCalculator.BuildPattern(Standard, new decimal?[] { 8m, 8m, 8m, 8m, 7.5m });
            var days = LeaveDayCalculator.Calculate(new DateTime(2026, 10, 12), new DateTime(2026, 10, 18), null, null, p);
            Assert.Equal(42m, days.Sum(d => d.Hours));
        }

        [Fact]
        public void PartTimer_OnlyTheirWorkingDays_AtTheStandardLeaveHours()
        {
            // Works Tue-Thu (Mon and Fri expected hours 0)
            var p = LeaveDayCalculator.BuildPattern(Standard, new decimal?[] { 0m, 8m, 8m, 8m, 0m });
            Assert.Equal(new[] { 0m, 8.5m, 8.5m, 8.5m, 0m, 0m, 0m }, p.Select(d => d.ExpectedHours).ToArray());
        }

        [Fact]
        public void NoColleagueConfiguration_UsesTheStandardWeek()
        {
            var p = LeaveDayCalculator.BuildPattern(Standard, null);
            Assert.Equal(Standard, p.Select(d => d.ExpectedHours).ToArray());
        }

        [Fact]
        public void DayNotFilledIn_CountsAsAWorkingDay()
        {
            var p = LeaveDayCalculator.BuildPattern(Standard, new decimal?[] { null, 8m, 8m, 8m, null });
            Assert.Equal(8.5m, p[0].ExpectedHours);
            Assert.Equal(8m, p[4].ExpectedHours);
        }

        [Fact]
        public void HalfDay_IsHalfTheLeaveDay()
        {
            var p = LeaveDayCalculator.BuildPattern(Standard, new decimal?[] { 8m, 8m, 8m, 8m, 7.5m });
            var mon = new DateTime(2026, 10, 12);
            var fri = new DateTime(2026, 10, 16);
            Assert.Equal(4.25m, LeaveDayCalculator.Calculate(mon, mon, null, null, p, null, startHalfDay: true).Single().Hours);
            Assert.Equal(4m, LeaveDayCalculator.Calculate(fri, fri, null, null, p, null, endHalfDay: true).Single().Hours);
        }

        [Fact]
        public void PartDayAppointment_StillUsesItsTimes()
        {
            var p = LeaveDayCalculator.BuildPattern(Standard, new decimal?[] { 8m, 8m, 8m, 8m, 7.5m },
                new[] { "08:30", "08:30", "08:30", "08:30", "08:30" }, new[] { "17:30", "17:30", "17:30", "17:30", "17:00" });
            var day = new DateTime(2026, 10, 14);
            Assert.Equal(1m, LeaveDayCalculator.Calculate(day, day, new TimeSpan(13, 45, 0), new TimeSpan(14, 45, 0), p).Single().Hours);
        }

        [Theory]
        [InlineData("8.5,8.5,8.5,8.5,8,0,0", 8.5, 8)]
        [InlineData("7.5;7.5;7.5;7.5;7.5;0;0", 7.5, 7.5)]
        [InlineData("rubbish", 8.5, 8)]
        [InlineData("", 8.5, 8)]
        [InlineData(null, 8.5, 8)]
        public void ParseLeaveDayHours(string text, double monday, double friday)
        {
            var h = LeaveDayCalculator.ParseLeaveDayHours(text);
            Assert.Equal((decimal)monday, h[0]);
            Assert.Equal((decimal)friday, h[4]);
        }
    }
}
