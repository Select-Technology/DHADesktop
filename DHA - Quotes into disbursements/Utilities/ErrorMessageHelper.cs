// USER-FRIENDLY ERROR MESSAGE HANDLER
// Add this utility class to handle and translate technical errors

using System;
using System.Windows;

namespace DHA.DSTC.WPF.Utilities
{
    public static class ErrorMessageHelper
    {
        /// <summary>
        /// Converts technical Dataverse errors into user-friendly messages
        /// </summary>
        /// <param name="technicalError">The raw error message from Dataverse/system</param>
        /// <returns>A user-friendly error message</returns>
        public static string GetFriendlyErrorMessage(string technicalError)
        {
            if (string.IsNullOrEmpty(technicalError))
                return "An unexpected error occurred.";

            var lowerError = technicalError.ToLowerInvariant();

            // Time entry validation errors
            if (lowerError.Contains("minutes") && lowerError.Contains("maximum value") && lowerError.Contains("60"))
            {
                return "Invalid time entry: Minutes must be between 0 and 59.\n\nIf you meant to enter more than 60 minutes, please convert to hours and minutes (e.g., 1 hour 30 minutes instead of 90 minutes).";
            }

            if (lowerError.Contains("hours") && lowerError.Contains("maximum"))
            {
                return "Invalid time entry: The number of hours is too large. Please enter a reasonable amount of time.";
            }

            if (lowerError.Contains("validation error") && lowerError.Contains("twp_minutes"))
            {
                return "Time entry error: Please check that minutes are entered correctly (0-59).";
            }

            // Databinding errors
            if (lowerError.Contains("binding") && lowerError.Contains("displayname"))
            {
                // Don't show binding errors to users - these are technical issues
                System.Diagnostics.Debug.WriteLine($"DataBinding error (hidden from user): {technicalError}");
                return null; // Don't show anything to the user
            }

            // General validation errors
            if (lowerError.Contains("validation error occurred"))
            {
                return "Please check your input and try again. Make sure all required fields are filled correctly.";
            }

            // Dataverse connection errors
            if (lowerError.Contains("connection") || lowerError.Contains("timeout"))
            {
                return "Connection issue: Please check your internet connection and try again.";
            }

            if (lowerError.Contains("unauthorized") || lowerError.Contains("access denied"))
            {
                return "Access denied: You may not have permission to perform this action.";
            }

            // If we can't translate it, return a generic but helpful message
            if (technicalError.Length > 200)
            {
                return "An error occurred while processing your request. Please try again or contact support if the problem persists.";
            }

            // For shorter, potentially more readable errors, return as-is but cleaned up
            return CleanupTechnicalMessage(technicalError);
        }

        /// <summary>
        /// Shows a user-friendly error dialog
        /// </summary>
        /// <param name="technicalError">The raw error message</param>
        /// <param name="title">Dialog title (optional)</param>
        /// <param name="context">Additional context about what the user was doing</param>
        public static void ShowFriendlyError(string technicalError, string title = "Error", string context = null)
        {
            var friendlyMessage = GetFriendlyErrorMessage(technicalError);

            // If the error was filtered out (like binding errors), don't show anything
            if (friendlyMessage == null) return;

            if (!string.IsNullOrEmpty(context))
            {
                friendlyMessage = $"{context}\n\n{friendlyMessage}";
            }

            MessageBox.Show(friendlyMessage, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// Cleans up technical messages to be more readable
        /// </summary>
        private static string CleanupTechnicalMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return "An error occurred.";

            // Remove common technical prefixes
            message = message.Replace("Error creating time entry: ", "");
            message = message.Replace("A validation error occurred. ", "");
            message = message.Replace("The value ", "");

            // Remove field names that users won't understand
            message = message.Replace("twp_minutes", "minutes");
            message = message.Replace("twp_hours", "hours");
            message = message.Replace("fwp_", "");

            // Capitalize first letter
            if (message.Length > 0)
            {
                message = char.ToUpper(message[0]) + message.Substring(1);
            }

            return message.Trim();
        }
    }
}