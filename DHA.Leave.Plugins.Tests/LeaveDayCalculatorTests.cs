using System;
using System.Collections.Generic;
using System.Linq;
using DHA.Leave.Plugins;
using Xunit;

namespace DHA.Leave.Plugins.Tests
{
    public class LeaveDayCalculatorTests
    {
        private static readonly WorkingDay[] Standard = LeaveDayCalculator.DefaultPattern();
        private static readonly TimeSpan? None = null;

        private static TimeSpan T(int h, int m = 0) => new TimeSpan(h, m, 0);

        [Fact]
        public void FullWeek_GivesFiveDaysOfSevenAndAHalf()
        {
            // Mon 12 Oct 2026 to Sun 18 Oct 2026
            var days = LeaveDayCalculator.Calculate(new DateTime(2026, 10, 12), new DateTime(2026, 10, 18), None, None, Standard);

            Assert.Equal(5, days.Count);
            Assert.All(days, d => Assert.Equal(7.5m, d.Hours));
            Assert.DoesNotContain(days, d => d.Date.DayOfWeek == DayOfWeek.Saturday || d.Date.DayOfWeek == DayOfWeek.Sunday);
        }

        [Fact]
        public void PartDay_ElevenToOne_IsTwoHours()
        {
            var day = new DateTime(2026, 10, 14);
            var days = LeaveDayCalculator.Calculate(day, day, T(11), T(13), Standard);

            Assert.Single(days);
            Assert.Equal(2m, days[0].Hours);
        }

        [Fact]
        public void WholeWorkingDayWindow_IsExpectedHoursNotElapsed()
        {
            // 09:00-17:30 is 8.5 elapsed, but the day is 7.5 (lunch)
            var day = new DateTime(2026, 10, 14);
            Assert.Equal(7.5m, LeaveDayCalculator.Calculate(day, day, T(9), T(17, 30), Standard).Single().Hours);
        }

        [Fact]
        public void AfternoonHalfDay_IsCappedAtWorkingDayFinish()
        {
            var day = new DateTime(2026, 10, 14);
            Assert.Equal(4m, LeaveDayCalculator.Calculate(day, day, T(13, 30), T(18), Standard).Single().Hours);
        }

        [Fact]
        public void MultiDay_WithTimes_AppliesStartToFirstDayAndEndToLastDay()
        {
            // Wed from 13:30, through Thu, to Fri 12:00
            var days = LeaveDayCalculator.Calculate(new DateTime(2026, 10, 14), new DateTime(2026, 10, 16), T(13, 30), T(12), Standard);

            Assert.Equal(new[] { 4m, 7.5m, 3m }, days.Select(d => d.Hours).ToArray());
        }

        [Fact]
        public void MidnightTimes_MeanWholeDays()
        {
            var days = LeaveDayCalculator.Calculate(new DateTime(2026, 10, 14), new DateTime(2026, 10, 15), T(0), T(0), Standard);
            Assert.Equal(new[] { 7.5m, 7.5m }, days.Select(d => d.Hours).ToArray());
        }

        [Fact]
        public void BankHolidays_AreSkipped()
        {
            var christmas = new DateTime(2026, 12, 25);
            var boxing = new DateTime(2026, 12, 28); // substitute day
            var days = LeaveDayCalculator.Calculate(new DateTime(2026, 12, 21), new DateTime(2026, 12, 31), None, None, Standard,
                new HashSet<DateTime> { christmas, boxing });

            Assert.DoesNotContain(days, d => d.Date == christmas || d.Date == boxing);
            Assert.Equal(7, days.Count);
        }

        [Fact]
        public void PartTimePattern_OnlyWorkingDaysAndTheirHours()
        {
            var pattern = LeaveDayCalculator.DefaultPattern();
            pattern[0] = WorkingDay.NotWorking();                                                          // Monday off
            pattern[4] = new WorkingDay { ExpectedHours = 3.75m, Start = T(9), Finish = T(12, 45) };       // Friday morning

            var days = LeaveDayCalculator.Calculate(new DateTime(2026, 10, 12), new DateTime(2026, 10, 16), None, None, pattern);

            Assert.Equal(4, days.Count);
            Assert.DoesNotContain(days, d => d.Date.DayOfWeek == DayOfWeek.Monday);
            Assert.Equal(3.75m, days.Single(d => d.Date.DayOfWeek == DayOfWeek.Friday).Hours);
        }

        [Fact]
        public void EndBeforeStart_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                LeaveDayCalculator.Calculate(new DateTime(2026, 10, 15), new DateTime(2026, 10, 14), None, None, Standard));
        }

        [Theory]
        [InlineData("09:00", false, 9, 0)]
        [InlineData("9.00", false, 9, 0)]
        [InlineData("08:30", false, 8, 30)]
        [InlineData("17:30", true, 17, 30)]
        [InlineData("5.30", true, 17, 30)]
        [InlineData("5:00", true, 17, 0)]
        public void ParseTime_HandlesColleagueConfigFormats(string text, bool isFinish, int h, int m)
        {
            Assert.Equal(T(h, m), LeaveDayCalculator.ParseTime(text, isFinish));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("noon")]
        [InlineData("25:00")]
        public void ParseTime_ReturnsNullForUnusableValues(string text)
        {
            Assert.Null(LeaveDayCalculator.ParseTime(text, false));
        }
    }
}
