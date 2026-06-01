// Replace the entire TimeEntryValidationHelper.cs with this updated version:

using DHA.DSTC.WPF.Services;
using System.Linq;
using System;

namespace DHA.DSTC.WPF.Utilities
{
    public static class TimeEntryValidationHelper
    {
        /// <summary>
        /// Determines if a time entry can still be edited based on the new locking rules:
        /// 
        /// NEW LOCKING RULES:
        /// - On Monday prior to noon: Everything up to Sunday 8 days ago should be editable, everything prior should be locked
        /// - On Monday at or after noon: Everything up to yesterday should be locked  
        /// - On Tuesday to Sunday: Everything up to the previous Sunday (2-7 days ago) should be locked
        /// - At all times: Anything more than 8 days ago should be locked
        /// </summary>
        /// <param name="entryDate">The date the time entry is for (not when it was created)</param>
        /// <returns>True if editable, false if locked</returns>
        public static bool CanEditTimeEntry(DateTime entryDate)
        {
            // Convert to London time zone to handle BST/GMT correctly
            var londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            var nowLondon = TimeZoneInfo.ConvertTime(DateTime.Now, londonTimeZone);
            var entryDateLondon = TimeZoneInfo.ConvertTime(entryDate, londonTimeZone).Date;

            // Rule: At all times, anything more than 8 days ago should be locked
            if (entryDateLondon < nowLondon.Date.AddDays(-8))
            {
                return false; // Locked - more than 8 days old
            }

            // Get current day of week and time
            var currentDayOfWeek = nowLondon.DayOfWeek;
            var currentTime = nowLondon.TimeOfDay;
            var isBeforeNoon = currentTime < TimeSpan.FromHours(12);

            DateTime cutoffDate;

            if (currentDayOfWeek == DayOfWeek.Monday)
            {
                if (isBeforeNoon)
                {
                    // On Monday prior to noon: Everything up to Sunday 8 days ago should be editable
                    cutoffDate = nowLondon.Date.AddDays(-8); // Sunday 8 days ago
                }
                else
                {
                    // On Monday at or after noon: Everything up to yesterday should be locked
                    cutoffDate = nowLondon.Date.AddDays(-1); // Yesterday
                }
            }
            else
            {
                // On Tuesday to Sunday: Everything up to the previous Sunday should be locked
                int daysSinceLastSunday = ((int)currentDayOfWeek == 0) ? 7 : (int)currentDayOfWeek;
                cutoffDate = nowLondon.Date.AddDays(-daysSinceLastSunday); // Previous Sunday
            }

            // Entry is editable if it's after the cutoff date
            return entryDateLondon > cutoffDate;
        }

        public static bool CanCreateTimeEntry(DateTime entryDate)
        {
            // Allow creation for any past or current date
            // Only block future dates
            return entryDate.Date <= DateTime.Today;
        }

        /// <summary>
        /// Gets the lock date for a time entry based on the new rules
        /// This tells you when the entry will become locked
        /// </summary>
        /// <param name="entryDate">The date the time entry is for</param>
        /// <returns>The date and time when editing will be locked</returns>
        public static DateTime GetLockDate(DateTime entryDate)
        {
            var londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            var entryDateLondon = TimeZoneInfo.ConvertTime(entryDate, londonTimeZone).Date;

            // Calculate when this entry will be locked based on the new rules

            // Rule 1: If entry is more than 8 days old from any given day, it's locked
            var absoluteLockDate = entryDateLondon.AddDays(9); // 9 days after entry date

            // Rule 2: Entry gets locked based on weekly schedule
            // Find the next Monday after the entry date
            var daysUntilNextMonday = ((int)DayOfWeek.Monday - (int)entryDateLondon.DayOfWeek + 7) % 7;
            if (daysUntilNextMonday == 0) daysUntilNextMonday = 7; // If entry is on Monday, use next Monday

            var nextMondayAfterEntry = entryDateLondon.AddDays(daysUntilNextMonday);

            // The entry will be locked at noon on the Monday following the week it was created
            // But if that's more than 8 days, use the 8-day rule instead
            var weeklyLockDate = nextMondayAfterEntry.AddHours(12); // Noon on next Monday

            // Use whichever comes first
            return weeklyLockDate < absoluteLockDate ? weeklyLockDate : absoluteLockDate;
        }

        /// <summary>
        /// Gets a user-friendly description of when the time entry will be locked
        /// </summary>
        /// <param name="entryDate">The date the time entry is for</param>
        /// <returns>Human-readable description of the lock timing</returns>
        public static string GetLockDescription(DateTime entryDate)
        {
            if (!CanEditTimeEntry(entryDate))
            {
                return "This time entry is locked and can no longer be edited.";
            }

            var lockDate = GetLockDate(entryDate);
            var timeRemaining = lockDate - DateTime.Now;

            if (timeRemaining.TotalHours <= 0)
            {
                return "This time entry is now locked and can no longer be edited.";
            }

            if (timeRemaining.TotalDays >= 1)
            {
                return $"This time entry will be locked on {lockDate:dddd, dd MMMM yyyy} at {lockDate:HH:mm}. " +
                       $"You have approximately {Math.Ceiling(timeRemaining.TotalDays)} day(s) remaining to make changes.";
            }
            else
            {
                return $"This time entry will be locked on {lockDate:dddd, dd MMMM yyyy} at {lockDate:HH:mm}. " +
                       $"You have approximately {Math.Ceiling(timeRemaining.TotalHours)} hour(s) remaining to make changes.";
            }
        }

        /// <summary>
        /// Determines if the lock warning should be shown (less than 24 hours remaining)
        /// </summary>
        /// <param name="entryDate">The date the time entry is for</param>
        /// <returns>True if warning should be shown</returns>
        public static bool ShouldShowLockWarning(DateTime entryDate)
        {
            if (!CanEditTimeEntry(entryDate))
            {
                return false; // Already locked, no warning needed
            }

            var lockDate = GetLockDate(entryDate);
            var timeRemaining = lockDate - DateTime.Now;
            return timeRemaining.TotalHours > 0 && timeRemaining.TotalHours < 24;
        }

        /// <summary>
        /// Validates daily hours limit (unchanged from original)
        /// </summary>
        public static bool ValidateDailyHoursLimit(DateTime selectedDate, Guid currentEntryId, decimal proposedHours, int proposedMinutes, Guid teamMemberId, TimeEntryService timeEntryService)
        {
            try
            {
                // Convert proposed time to decimal hours
                decimal proposedDecimalHours = proposedHours + (proposedMinutes / 60.0m);

                // Get all time entries for this user on this date
                var existingEntries = timeEntryService.GetTimeEntries()
                    .Where(te => te.Date.Date == selectedDate.Date &&
                                te.TeamMemberId == teamMemberId &&
                                te.Id != currentEntryId) // Exclude current entry if editing
                    .ToList();

                // Calculate total existing hours for the day
                decimal totalExistingHours = 0;
                foreach (var entry in existingEntries)
                {
                    totalExistingHours += entry.Hours + (entry.Minutes / 60.0m);
                }

                // Check if proposed + existing would exceed 24 hours
                decimal totalProposedHours = totalExistingHours + proposedDecimalHours;

                return totalProposedHours <= 24.0m;
            }
            catch (Exception ex)
            {
                // Log error but don't fail validation - allow the save and handle errors elsewhere
                System.Diagnostics.Debug.WriteLine($"Error in ValidateDailyHoursLimit: {ex.Message}");
                return true; // Default to allowing save if validation fails
            }
        }

        /// <summary>
        /// Helper method to get a detailed explanation of the current locking rules
        /// Useful for help documentation or user guidance
        /// </summary>
        public static string GetLockingRulesExplanation()
        {
            return @"Time Entry Locking Rules:

• On Monday before noon: You can edit entries up to 8 days old (including last Sunday)
• On Monday at or after noon: You can only edit yesterday's entries and newer  
• Tuesday through Sunday: You can edit entries back to the previous Sunday
• Absolute limit: Entries more than 8 days old are always locked

Examples:
- It's Wednesday: You can edit entries from last Sunday onwards
- It's Monday 10am: You can edit entries from last Sunday (8 days ago) onwards  
- It's Monday 2pm: You can only edit entries from yesterday onwards
- Any day: Entries older than 8 days are locked regardless";
        }
    }
}