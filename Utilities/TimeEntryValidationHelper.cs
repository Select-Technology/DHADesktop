using System;

namespace DHA.DSTC.WPF.Utilities
{
    public static class TimeEntryValidationHelper
    {
        /// <summary>
        /// Determines if a time entry can still be edited based on the creation date
        /// </summary>
        /// <param name="createdDate">When the time entry was created</param>
        /// <returns>True if editable, false if locked</returns>
        public static bool CanEditTimeEntry(DateTime createdDate)
        {
            // Convert to London time zone to handle BST/GMT correctly
            var londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            var nowLondon = TimeZoneInfo.ConvertTime(DateTime.Now, londonTimeZone);
            var createdLondon = TimeZoneInfo.ConvertTime(createdDate, londonTimeZone);

            // Find the Monday following the creation date
            var daysUntilMonday = ((int)DayOfWeek.Monday - (int)createdLondon.DayOfWeek + 7) % 7;
            if (daysUntilMonday == 0 && createdLondon.DayOfWeek != DayOfWeek.Monday)
            {
                daysUntilMonday = 7; // If created on Monday, go to next Monday
            }

            var lockDate = createdLondon.Date.AddDays(daysUntilMonday).AddHours(12); // 12 noon on Monday

            return nowLondon < lockDate;
        }

        /// <summary>
        /// Gets the lock date for a time entry
        /// </summary>
        /// <param name="createdDate">When the time entry was created</param>
        /// <returns>The date and time when editing will be locked</returns>
        public static DateTime GetLockDate(DateTime createdDate)
        {
            var londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            var createdLondon = TimeZoneInfo.ConvertTime(createdDate, londonTimeZone);

            var daysUntilMonday = ((int)DayOfWeek.Monday - (int)createdLondon.DayOfWeek + 7) % 7;
            if (daysUntilMonday == 0 && createdLondon.DayOfWeek != DayOfWeek.Monday)
            {
                daysUntilMonday = 7;
            }

            return createdLondon.Date.AddDays(daysUntilMonday).AddHours(12);
        }
    }
}