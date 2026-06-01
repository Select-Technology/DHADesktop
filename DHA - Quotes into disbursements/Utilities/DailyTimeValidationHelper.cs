using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DHA.DSTC.WPF.Utilities
{
    public static class DailyTimeValidationHelper
    {
        private const decimal MAX_DAILY_HOURS = 24.0m;
        private const decimal MAX_SINGLE_ENTRY_HOURS = 16.0m;
        private const decimal NEAR_LIMIT_THRESHOLD = 22.0m; // Show warning when approaching limit

        private static string FormatHoursMinutes(decimal totalHours)
        {
            if (totalHours == 0) return "0m";
            int wholeHours = (int)Math.Floor(totalHours);
            int minutes = (int)Math.Round((totalHours - wholeHours) * 60);
            if (wholeHours == 0) return $"{minutes}m";
            if (minutes == 0) return $"{wholeHours}h";
            return $"{wholeHours}h {minutes}m";
        }

        /// <summary>
        /// Validates that adding a new time entry won't exceed 24 hours for the day
        /// </summary>
        /// <param name="date">The date for the time entry</param>
        /// <param name="newHours">Hours to add</param>
        /// <param name="newMinutes">Minutes to add</param>
        /// <param name="teamMemberId">Team member ID</param>
        /// <param name="existingEntries">Collection of existing time entries</param>
        /// <param name="excludeEntryId">Entry ID to exclude (for edits)</param>
        /// <returns>Validation result with success status and message</returns>
        public static ValidationResult ValidateDailyTimeLimit(
            DateTime date,
            decimal newHours,
            int newMinutes,
            Guid teamMemberId,
            IEnumerable<TimeEntry> existingEntries,
            Guid? excludeEntryId = null)
        {
            try
            {
                // Convert new time to total hours
                decimal newTotalHours = newHours + (newMinutes / 60.0m);

                // First validate single entry doesn't exceed reasonable maximum
                if (newTotalHours > MAX_SINGLE_ENTRY_HOURS)
                {
                    return new ValidationResult(false,
                        $"Individual time entries cannot exceed {MAX_SINGLE_ENTRY_HOURS} hours.\n\n" +
                        "💡 For longer periods, please split across multiple entries or days.");
                }

                // Calculate existing hours for this date and team member
                // FIXED: Use Hours and Minutes properties instead of TotalHours
                decimal existingHoursForDay = existingEntries
                    .Where(te => te.Date.Date == date.Date &&
                                 te.TeamMemberId == teamMemberId &&
                                 (!excludeEntryId.HasValue || te.IdGuid != excludeEntryId.Value))
                    .Sum(te => te.Hours + (te.Minutes / 60.0m)); // FIXED: Calculate from Hours and Minutes

                decimal totalHoursForDay = existingHoursForDay + newTotalHours;

                // Check if exceeds 24 hours
                if (totalHoursForDay > MAX_DAILY_HOURS)
                {
                    decimal excessHours = totalHoursForDay - MAX_DAILY_HOURS;
                    int excessHoursInt = (int)Math.Floor(excessHours);
                    int excessMinutes = (int)Math.Round((excessHours - excessHoursInt) * 60);

                    string message = $"⚠️ Daily Time Limit Exceeded\n\n" +
                                   $"Adding this time entry would result in {FormatHoursMinutes(totalHoursForDay)} for {date:dddd, dd MMMM yyyy}.\n\n" +
                                   $"📊 Current breakdown:\n" +
                                   $"• Already logged: {FormatHoursMinutes(existingHoursForDay)}\n" +
                                   $"• Attempting to add: {FormatHoursMinutes(newTotalHours)}\n" +
                                   $"• Total would be: {FormatHoursMinutes(totalHoursForDay)}\n" +
                                   $"• Exceeds limit by: {FormatHoursMinutes(excessHours)}\n\n" +
                                   $"💡 Suggestions:\n" +
                                   $"• Reduce this entry by {excessHoursInt}h {excessMinutes}m\n" +
                                   $"• Split time across multiple days\n" +
                                   $"• Check if existing entries need adjustment";

                    return new ValidationResult(false, message);
                }

                // Show warning if approaching limit
                if (totalHoursForDay >= NEAR_LIMIT_THRESHOLD && totalHoursForDay < MAX_DAILY_HOURS)
                {
                    string warningMessage = $"⚠️ Approaching Daily Limit\n\n" +
                                          $"After this entry: {FormatHoursMinutes(totalHoursForDay)} / 24h\n" +
                                          $"Remaining: {FormatHoursMinutes(MAX_DAILY_HOURS - totalHoursForDay)}";

                    // Return as valid but with warning message
                    return new ValidationResult(true, warningMessage);
                }

                // Success - within limits
                return new ValidationResult(true, $"Time entry valid. Daily total: {FormatHoursMinutes(totalHoursForDay)} / 24h");
            }
            catch (Exception ex)
            {
                // Log the error but allow the entry - don't fail validation due to calculation errors
                System.Diagnostics.Debug.WriteLine($"Error in ValidateDailyTimeLimit: {ex.Message}");
                return new ValidationResult(true, "Validation check completed with warnings.");
            }
        }

        /// <summary>
        /// Gets a comprehensive summary of time logged for a specific date and team member
        /// </summary>
        public static DailyTimeSummary GetDailyTimeSummary(
            DateTime date,
            Guid teamMemberId,
            IEnumerable<TimeEntry> existingEntries,
            Guid? excludeEntryId = null)
        {
            var relevantEntries = existingEntries
                .Where(te => te.Date.Date == date.Date &&
                            te.TeamMemberId == teamMemberId &&
                            (!excludeEntryId.HasValue || te.IdGuid != excludeEntryId.Value))
                .ToList();

            // FIXED: Use Hours and Minutes properties
            decimal totalHours = relevantEntries.Sum(te => te.Hours + (te.Minutes / 60.0m));
            decimal remainingHours = Math.Max(0, MAX_DAILY_HOURS - totalHours);

            return new DailyTimeSummary
            {
                Date = date,
                TeamMemberId = teamMemberId,
                TotalHoursLogged = totalHours,
                RemainingHours = remainingHours,
                EntryCount = relevantEntries.Count,
                IsAtLimit = totalHours >= MAX_DAILY_HOURS,
                IsNearLimit = totalHours >= NEAR_LIMIT_THRESHOLD,
                Entries = relevantEntries
            };
        }

        /// <summary>
        /// Suggests maximum safe entry without exceeding daily limit
        /// </summary>
        public static TimeEntrySuggestion SuggestMaximumSafeEntry(
            DateTime date,
            Guid teamMemberId,
            IEnumerable<TimeEntry> existingEntries,
            Guid? excludeEntryId = null)
        {
            var summary = GetDailyTimeSummary(date, teamMemberId, existingEntries, excludeEntryId);

            if (summary.RemainingHours <= 0)
            {
                return new TimeEntrySuggestion
                {
                    MaxHours = 0,
                    MaxMinutes = 0,
                    Message = "❌ No remaining time available for this date.\n\nDaily limit of 24 hours already reached."
                };
            }

            // Don't suggest more than reasonable single entry maximum
            decimal suggestedHours = Math.Min(summary.RemainingHours, MAX_SINGLE_ENTRY_HOURS);

            int maxHours = (int)Math.Floor(suggestedHours);
            int maxMinutes = (int)Math.Round((suggestedHours - maxHours) * 60);

            return new TimeEntrySuggestion
            {
                MaxHours = maxHours,
                MaxMinutes = maxMinutes,
                Message = $"💡 Maximum recommended entry: {maxHours}h {maxMinutes}m\n\n" +
                         $"Daily summary:\n" +
                         $"• Already logged: {FormatHoursMinutes(summary.TotalHoursLogged)}\n" +
                         $"• Remaining today: {FormatHoursMinutes(summary.RemainingHours)}"
            };
        }

        /// <summary>
        /// Legacy method for backward compatibility with TimeEntryService
        /// </summary>
        public static bool ValidateDailyHoursLimit(
            DateTime selectedDate,
            Guid currentEntryId,
            decimal proposedHours,
            int proposedMinutes,
            Guid teamMemberId,
            TimeEntryService timeEntryService)
        {
            try
            {
                var existingEntries = timeEntryService.GetTimeEntries();
                var result = ValidateDailyTimeLimit(
                    selectedDate,
                    proposedHours,
                    proposedMinutes,
                    teamMemberId,
                    existingEntries,
                    currentEntryId);

                return result.IsValid;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ValidateDailyHoursLimit: {ex.Message}");
                return true; // Default to allowing save if validation fails
            }
        }

        /// <summary>
        /// Gets total daily hours for backward compatibility
        /// </summary>
        public static decimal GetDailyHoursTotal(
            DateTime selectedDate,
            Guid currentEntryId,
            Guid teamMemberId,
            TimeEntryService timeEntryService)
        {
            try
            {
                var existingEntries = timeEntryService.GetTimeEntries();
                var summary = GetDailyTimeSummary(selectedDate, teamMemberId, existingEntries, currentEntryId);
                return summary.TotalHoursLogged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetDailyHoursTotal: {ex.Message}");
                return 0;
            }
        }
    }

    // Supporting classes for the validation system
    public class ValidationResult
    {
        public bool IsValid { get; }
        public string Message { get; }
        public bool IsWarning { get; }

        public ValidationResult(bool isValid, string message, bool isWarning = false)
        {
            IsValid = isValid;
            Message = message;
            IsWarning = isWarning;
        }
    }

    public class DailyTimeSummary
    {
        public DateTime Date { get; set; }
        public Guid TeamMemberId { get; set; }
        public decimal TotalHoursLogged { get; set; }
        public decimal RemainingHours { get; set; }
        public int EntryCount { get; set; }
        public bool IsAtLimit { get; set; }
        public bool IsNearLimit { get; set; }
        public List<TimeEntry> Entries { get; set; } = new List<TimeEntry>();
    }

    public class TimeEntrySuggestion
    {
        public int MaxHours { get; set; }
        public int MaxMinutes { get; set; }
        public string Message { get; set; }
    }
}