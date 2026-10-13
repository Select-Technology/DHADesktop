using System;
using System.Collections.Generic;
using System.Globalization;

namespace DHA.Leave.Plugins
{
    /// <summary>One weekday of a colleague's working pattern (from fwp_colleagueconfiguration).</summary>
    public class WorkingDay
    {
        public decimal ExpectedHours { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan Finish { get; set; }

        public static WorkingDay Standard(decimal hours) =>
            new WorkingDay { ExpectedHours = hours, Start = new TimeSpan(9, 0, 0), Finish = new TimeSpan(17, 30, 0) };

        public static WorkingDay NotWorking() =>
            new WorkingDay { ExpectedHours = 0m, Start = TimeSpan.Zero, Finish = TimeSpan.Zero };
    }

    /// <summary>A single day's leave, ready to become one fwp_timeentry.</summary>
    public class LeaveDay
    {
        public DateTime Date { get; set; }
        public decimal Hours { get; set; }
    }

    /// <summary>
    /// Turns an approved leave request (date range plus optional start/end times) into per-day hours,
    /// using the person's working pattern. Pure logic - no Dataverse calls - so it is unit tested and
    /// shared by the SharePoint-driven flow (phase A) and the desktop leave feature (phase B).
    /// </summary>
    public static class LeaveDayCalculator
    {
        public const decimal DefaultDayHours = 7.5m;

        /// <summary>Mon-Fri at the standard day length, weekends off.</summary>
        public static WorkingDay[] DefaultPattern(decimal dayHours = DefaultDayHours)
        {
            var pattern = new WorkingDay[7];
            for (int i = 0; i < 7; i++)
                pattern[i] = i < 5 ? WorkingDay.Standard(dayHours) : WorkingDay.NotWorking();
            return pattern;
        }

        /// <summary>Index into a Monday-first pattern array.</summary>
        public static int PatternIndex(DayOfWeek day) => ((int)day + 6) % 7;

        /// <param name="pattern">Seven days, Monday first.</param>
        /// <param name="startTime">Time leave starts on the first day; null means the start of that working day.</param>
        /// <param name="endTime">Time leave ends on the last day; null (or midnight) means the end of that working day.</param>
        /// <param name="skipDates">Dates that never get a leave entry (bank holidays, which get their own entry).</param>
        public static List<LeaveDay> Calculate(DateTime startDate, DateTime endDate,
            TimeSpan? startTime, TimeSpan? endTime, WorkingDay[] pattern, ISet<DateTime> skipDates = null)
        {
            if (pattern == null || pattern.Length != 7)
                throw new ArgumentException("Working pattern must have seven days, Monday first.", nameof(pattern));

            startDate = startDate.Date;
            endDate = endDate.Date;
            if (endDate < startDate)
                throw new ArgumentException("End date is before start date.");

            // Midnight as an end time means "to the end of the day"
            if (endTime.HasValue && endTime.Value == TimeSpan.Zero)
                endTime = null;

            var days = new List<LeaveDay>();
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                if (skipDates != null && skipDates.Contains(date))
                    continue;

                var workingDay = pattern[PatternIndex(date.DayOfWeek)];
                if (workingDay == null || workingDay.ExpectedHours <= 0m)
                    continue;

                var from = date == startDate && startTime.HasValue ? startTime.Value : workingDay.Start;
                var to = date == endDate && endTime.HasValue ? endTime.Value : workingDay.Finish;

                decimal hours = HoursForWindow(workingDay, from, to);
                if (hours > 0m)
                    days.Add(new LeaveDay { Date = date, Hours = hours });
            }
            return days;
        }

        /// <summary>
        /// Hours of leave on one day for the window [from, to]. Covering the whole working day gives that
        /// day's expected hours (so a 09:00-17:30 day with a break is still 7.5). A part day gives the
        /// overlap with the working day, e.g. 11:00-13:00 is 2 hours, capped at the expected hours.
        /// </summary>
        public static decimal HoursForWindow(WorkingDay day, TimeSpan from, TimeSpan to)
        {
            // No usable start/finish recorded - assume the standard office day
            if (day.Finish <= day.Start)
                day = new WorkingDay { ExpectedHours = day.ExpectedHours, Start = new TimeSpan(9, 0, 0), Finish = new TimeSpan(17, 30, 0) };

            if (from <= day.Start && to >= day.Finish)
                return day.ExpectedHours;

            var overlapStart = from > day.Start ? from : day.Start;
            var overlapEnd = to < day.Finish ? to : day.Finish;
            if (overlapEnd <= overlapStart)
                return 0m;

            return Math.Min(day.ExpectedHours, Hours(overlapEnd - overlapStart));
        }

        private static decimal Hours(TimeSpan span) => Math.Round((decimal)span.TotalMinutes / 60m, 2);

        /// <summary>
        /// Parses the free-text start/finish times on colleague configuration ("09:00", "9.00", "5:30").
        /// Finish times before 08:00 are taken as afternoon ("5.30" finish = 17:30).
        /// </summary>
        public static TimeSpan? ParseTime(string text, bool isFinish)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var parts = text.Trim().Replace('.', ':').Split(':');
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                return null;
            int m = 0;
            if (parts.Length > 1 && !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out m))
                return null;
            if (h < 0 || h > 23 || m < 0 || m > 59)
                return null;
            if (isFinish && h < 8)
                h += 12;
            return new TimeSpan(h, m, 0);
        }
    }
}
