using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DHA.DSTC.Models;
using DHA.DSTC.Services;
using DHA.DSTC.Utilities;

namespace DHA.DSTC.ViewModels
{
    /// <summary>
    /// View model for calendar operations
    /// </summary>
    public class CalendarViewModel
    {
        private readonly CalendarService _calendarService;
        private readonly TeamMemberService _teamMemberService;

        /// <summary>
        /// Initializes a new instance of the CalendarViewModel class
        /// </summary>
        public CalendarViewModel()
        {
            _calendarService = ServiceLocator.CalendarService;
            _teamMemberService = ServiceLocator.TeamMemberService;
        }

        /// <summary>
        /// Gets all team members
        /// </summary>
        /// <returns>List of team members</returns>
        public List<TeamMember> GetTeamMembers()
        {
            return _teamMemberService.GetTeamMembers();
        }

        /// <summary>
        /// Gets the time entries for a month as a dictionary of date to hours
        /// </summary>
        /// <param name="teamMemberId">Team member ID</param>
        /// <param name="month">Month to get entries for</param>
        /// <returns>Dictionary mapping dates to total hours</returns>
        public Dictionary<DateTime, decimal> GetMonthlyTimeEntries(Guid teamMemberId, DateTime month)
        {
            return _calendarService.GetMonthlyTimeEntries(teamMemberId, month);
        }

        /// <summary>
        /// Gets a summary of time entries by week for a month
        /// </summary>
        /// <param name="teamMemberId">Team member ID</param>
        /// <param name="month">Month to get entries for</param>
        /// <returns>Dictionary mapping week numbers to total hours</returns>
        public Dictionary<int, decimal> GetMonthSummary(Guid teamMemberId, DateTime month)
        {
            return _calendarService.GetMonthSummary(teamMemberId, month);
        }

        /// <summary>
        /// Gets the total hours for a month
        /// </summary>
        /// <param name="teamMemberId">Team member ID</param>
        /// <param name="month">Month to get total for</param>
        /// <returns>Total hours for the month</returns>
        public decimal GetMonthTotal(Guid teamMemberId, DateTime month)
        {
            return _calendarService.GetMonthTotal(teamMemberId, month);
        }

        /// <summary>
        /// Gets the days in a month
        /// </summary>
        /// <param name="year">Year</param>
        /// <param name="month">Month</param>
        /// <returns>Number of days in the month</returns>
        public int GetDaysInMonth(int year, int month)
        {
            return DateTime.DaysInMonth(year, month);
        }

        /// <summary>
        /// Gets the first day of the week for a month
        /// </summary>
        /// <param name="year">Year</param>
        /// <param name="month">Month</param>
        /// <returns>Day of week for the first day of the month</returns>
        public DayOfWeek GetFirstDayOfMonth(int year, int month)
        {
            return new DateTime(year, month, 1).DayOfWeek;
        }

        /// <summary>
        /// Checks if a date is today
        /// </summary>
        /// <param name="date">Date to check</param>
        /// <returns>True if the date is today, false otherwise</returns>
        public bool IsToday(DateTime date)
        {
            return date.Date == DateTime.Today;
        }

        /// <summary>
        /// Checks if a month is in the future
        /// </summary>
        /// <param name="year">Year</param>
        /// <param name="month">Month</param>
        /// <returns>True if the month is in the future, false otherwise</returns>
        public bool IsFutureMonth(int year, int month)
        {
            var date = new DateTime(year, month, 1);
            var today = DateTime.Today;

            return date.Year > today.Year || (date.Year == today.Year && date.Month > today.Month);
        }

        /// <summary>
        /// Gets a color code for hours worked
        /// </summary>
        /// <param name="hours">Hours worked</param>
        /// <returns>Color code: 0 = None, 1 = Low, 2 = Medium, 3 = High</returns>
        public int GetHoursColorCode(decimal hours)
        {
            if (hours <= 0)
                return 0;
            if (hours < 3)
                return 1;
            if (hours < 7)
                return 2;
            return 3;
        }
    }
}