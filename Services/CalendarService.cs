using System;
using System.Collections.Generic;
using System.Linq;
using DHA.DSTC.Models;

namespace DHA.DSTC.Services
{
    public class CalendarService
    {
        private readonly TimeEntryService _timeEntryService;

        public CalendarService(TimeEntryService timeEntryService)
        {
            _timeEntryService = timeEntryService;
        }

        public Dictionary<DateTime, decimal> GetMonthlyTimeEntries(Guid teamMemberId, DateTime month)
        {
            // Get all time entries
            var allTimeEntries = _timeEntryService.GetTimeEntries();

            // Filter by team member
            var teamMemberEntries = allTimeEntries
                .Where(t => t.TeamMemberId == teamMemberId)
                .ToList();

            // Get start and end dates for the month
            var firstDayOfMonth = new DateTime(month.Year, month.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            // Filter entries by month and group by date
            var entriesByDate = teamMemberEntries
                .Where(t => t.Date >= firstDayOfMonth && t.Date <= lastDayOfMonth)
                .GroupBy(t => t.Date.Date)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(t => t.TotalHours)
                );

            return entriesByDate;
        }

        public Dictionary<int, decimal> GetMonthSummary(Guid teamMemberId, DateTime month)
        {
            // Get daily entries first
            var dailyEntries = GetMonthlyTimeEntries(teamMemberId, month);

            // Group by week number
            var weekSummary = new Dictionary<int, decimal>();

            foreach (var entry in dailyEntries)
            {
                // Calculate week of month (1-based)
                int weekOfMonth = (entry.Key.Day - 1) / 7 + 1;

                if (!weekSummary.ContainsKey(weekOfMonth))
                {
                    weekSummary[weekOfMonth] = 0;
                }

                weekSummary[weekOfMonth] += entry.Value;
            }

            return weekSummary;
        }

        public decimal GetMonthTotal(Guid teamMemberId, DateTime month)
        {
            var dailyEntries = GetMonthlyTimeEntries(teamMemberId, month);
            return dailyEntries.Values.Sum();
        }
    }
}