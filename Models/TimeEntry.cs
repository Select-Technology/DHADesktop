using System;
using Microsoft.Xrm.Sdk;

namespace DHA.DSTC.WPF.Models
{
    public class TimeEntry
    {
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
        public decimal Hours { get; set; }
        public int Minutes { get; set; }
        public string Comments { get; set; }
        public Guid ProjectId { get; set; }
        public Guid TeamMemberId { get; set; }

        // Navigation properties
        public string ProjectName { get; set; }

        // Calculated property for total hours
        public decimal TotalHours => Hours + (decimal)Minutes / 60;

        public TimeEntry()
        {
            Date = DateTime.Today;
            Id = Guid.Empty;
        }

        public override string ToString()
        {
            // Format: DD/MM/YYYY - X.X hours
            decimal totalHours = Hours + (decimal)Minutes / 60;
            string hoursText = totalHours == 1 ? "1 hour" : $"{totalHours:0.#} hours";

            return $"{Date:dd/MM/yyyy} - {hoursText}";
        }

        // Alternative: If you want to show exactly as requested (e.g., "4.5 hours")
        public string ToDisplayString()
        {
            string dateFormatted = Date.ToString("dd/MM/yyyy");
            decimal totalHours = Hours + (decimal)Minutes / 60;
            return $"{dateFormatted} - {totalHours} hours";
        }

        // Convert from Dataverse Entity to TimeEntry model
        public static TimeEntry FromEntity(Entity entity)
        {
            if (entity == null)
                return null;

            try
            {
                var timeEntry = new TimeEntry
                {
                    Id = entity.Id
                };

                // Handle Date (with fallback to current date if missing)
                try
                {
                    if (entity.Contains("fwp_date"))
                        timeEntry.Date = entity.GetAttributeValue<DateTime>("fwp_date").Date;
                    else
                        timeEntry.Date = DateTime.Today;
                }
                catch
                {
                    timeEntry.Date = DateTime.Today;
                }

                // Handle numeric values (with fallback to 0 if missing)
                try
                {
                    timeEntry.Hours = entity.Contains("fwp_decimalhours") ? entity.GetAttributeValue<decimal>("fwp_decimalhours") : 0;
                }
                catch
                {
                    timeEntry.Hours = 0;
                }

                try
                {
                    timeEntry.Minutes = entity.Contains("fwp_minutes") ? entity.GetAttributeValue<int>("fwp_minutes") : 0;
                }
                catch
                {
                    timeEntry.Minutes = 0;
                }

                // Handle string values
                timeEntry.Comments = entity.Contains("fwp_notes") ? entity.GetAttributeValue<string>("fwp_notes") : string.Empty;

                // Handle lookup references
                try
                {
                    if (entity.Contains("fwp_project"))
                    {
                        var projectRef = entity.GetAttributeValue<EntityReference>("fwp_project");
                        if (projectRef != null)
                        {
                            timeEntry.ProjectId = projectRef.Id;
                            timeEntry.ProjectName = projectRef.Name ?? "Unknown Project";
                        }
                    }
                }
                catch
                {
                    timeEntry.ProjectName = "Error loading project";
                }

                try
                {
                    if (entity.Contains("fwp_teammember"))
                    {
                        var teamMemberRef = entity.GetAttributeValue<EntityReference>("fwp_teammember");
                        if (teamMemberRef != null)
                        {
                            timeEntry.TeamMemberId = teamMemberRef.Id;
                        }
                    }
                }
                catch
                {
                    // Ignore team member errors
                }

                return timeEntry;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error converting entity to TimeEntry: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return new TimeEntry();
            }
        }

        // Convert from TimeEntry model to Dataverse Entity - Handle quote field requirement
        public Entity ToEntity()
        {
            var entity = new Entity("fwp_timeentry");

            if (Id != Guid.Empty)
                entity.Id = Id;

            // Set required fields
            entity["fwp_date"] = Date.Date;
            entity["fwp_decimalhours"] = Hours;
            entity["fwp_minutes"] = Minutes;

            // Set notes (empty string if null to avoid issues)
            entity["fwp_notes"] = Comments ?? string.Empty;

            // Set project reference if we have a valid ID
            if (ProjectId != Guid.Empty)
            {
                entity["fwp_project"] = new EntityReference("msdyn_project", ProjectId);
            }

            // Set team member reference if we have a valid ID
            if (TeamMemberId != Guid.Empty)
            {
                entity["fwp_teammember"] = new EntityReference("systemuser", TeamMemberId);
            }

            // Explicitly set fwp_quote to null to satisfy any business rules
            // This handles cases where the field is "optional" but workflows expect it to be explicitly set
            entity["fwp_quote"] = null;

            return entity;
        }
    }
}