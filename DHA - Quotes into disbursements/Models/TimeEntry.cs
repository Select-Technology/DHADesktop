using Microsoft.Xrm.Sdk;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using DHA.DSTC.WPF.Utilities;

namespace DHA.DSTC.WPF.Models
{
    public enum TimeEntryCategory
    {
        Chargeable = 1,
        NonChargeable = 2,
        Speculative = 3,
        HourlyRate = 4
    }

    public enum TimeEntryClassification
    {
        Project = 800470000,
        Quote = 800470001
    }

    public class TimeEntry : INotifyPropertyChanged
    {
        // Private backing fields
        private Guid _id;
        private DateTime _date;
        private DateTime _createdDate; // ? NEW: For lock calculation
        private decimal _hours;
        private int _minutes;
        private string _comments;
        private Guid _projectId;
        private Guid _quoteId;
        private Guid _teamMemberId;
        private Guid _idGuid;
        private string _projectName;
        private string _projectNumber;
        private string _quoteName;
        private string _quoteNumber;
        private string _clientName;
        private TimeEntryCategory _category = TimeEntryCategory.Chargeable;
        private TimeEntryClassification _classification = TimeEntryClassification.Project;

        #region Properties

        // Classification property
        public TimeEntryClassification Classification
        {
            get => _classification;
            set { _classification = value; OnPropertyChanged(); OnPropertyChanged(nameof(ClassificationName)); }
        }

        public string ClassificationName
        {
            get
            {
                switch (Classification)
                {
                    case TimeEntryClassification.Project:
                        return "Project";
                    case TimeEntryClassification.Quote:
                        return "Quote";
                    default:
                        return "Unknown";
                }
            }
        }

        // Quote-related properties
        public Guid QuoteId
        {
            get => _quoteId;
            set { _quoteId = value; OnPropertyChanged(); }
        }

        public string QuoteName
        {
            get => _quoteName;
            set { _quoteName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        public string QuoteNumber
        {
            get => _quoteNumber;
            set { _quoteNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        // Category property
        public TimeEntryCategory Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); OnPropertyChanged(nameof(CategoryName)); }
        }

        public string CategoryName
        {
            get
            {
                switch (Category)
                {
                    case TimeEntryCategory.Chargeable:
                        return "Chargeable";
                    case TimeEntryCategory.NonChargeable:
                        return "Non-Chargeable";
                    case TimeEntryCategory.Speculative:
                        return "Speculative";
                    case TimeEntryCategory.HourlyRate:
                        return "Hourly Rate";
                    default:
                        return "Unknown";
                }
            }
        }

        // Core properties
        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public DateTime Date
        {
            get => _date;
            set { _date = value; OnPropertyChanged(); }
        }

        // ? NEW: CreatedDate property for lock calculation
        public DateTime CreatedDate
        {
            get => _createdDate;
            set
            {
                _createdDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LockDate));
                OnPropertyChanged(nameof(IsLocked));
                OnPropertyChanged(nameof(IsEditable));
            }
        }

        public decimal Hours
        {
            get => _hours;
            set { _hours = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalTime)); OnPropertyChanged(nameof(TotalHours)); }
        }

        public int Minutes
        {
            get => _minutes;
            set { _minutes = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalTime)); OnPropertyChanged(nameof(TotalHours)); }
        }

        public string Comments
        {
            get => _comments;
            set { _comments = value; OnPropertyChanged(); }
        }

        public Guid ProjectId
        {
            get => _projectId;
            set { _projectId = value; OnPropertyChanged(); }
        }

        public Guid TeamMemberId
        {
            get => _teamMemberId;
            set { _teamMemberId = value; OnPropertyChanged(); }
        }

        public Guid IdGuid
        {
            get => _idGuid;
            set { _idGuid = value; OnPropertyChanged(); }
        }

        public string ProjectName
        {
            get => _projectName;
            set { _projectName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        public string ProjectNumber
        {
            get => _projectNumber;
            set { _projectNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        public string ClientName
        {
            get => _clientName;
            set { _clientName = value; OnPropertyChanged(); }
        }

        // Display name that shows either project or quote information
        public string DisplayName
        {
            get
            {
                if (Classification == TimeEntryClassification.Quote && !string.IsNullOrEmpty(QuoteName))
                {
                    return $"{QuoteNumber} - {QuoteName}";
                }
                else if (!string.IsNullOrEmpty(ProjectName))
                {
                    return $"{ProjectNumber} - {ProjectName}";
                }
                return "Unknown";
            }
        }

        // Helper properties
        public string TotalTime
        {
            get
            {
                if (Hours == 0 && Minutes == 0)
                    return "0m";

                if (Hours == 0)
                    return $"{Minutes}m";

                if (Minutes == 0)
                    return $"{Hours:0}h";

                return $"{Hours:0}h {Minutes}m";
            }
        }

        public decimal TotalHours => Hours + (Minutes / 60m);

        // ? UPDATED: Lock-related properties using CreatedDate and TimeEntryValidationHelper
        public DateTime LockDate
        {
            get
            {
                return TimeEntryValidationHelper.GetLockDate(Date); // Use entry Date, not CreatedDate
            }
        }

        public bool IsLocked => !TimeEntryValidationHelper.CanEditTimeEntry(Date); // Use entry Date, not CreatedDate
        public bool IsEditable => TimeEntryValidationHelper.CanEditTimeEntry(Date); // Use entry Date, not CreatedDate

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets a user-friendly description of when the time entry will be locked
        /// </summary>
        public string GetLockDescription()
        {
            return TimeEntryValidationHelper.GetLockDescription(Date); // Use entry Date, not CreatedDate
        }

        /// <summary>
        /// Determines if the lock warning should be shown (less than 24 hours remaining)
        /// </summary>
        public bool ShouldShowLockWarning()
        {
            return TimeEntryValidationHelper.ShouldShowLockWarning(Date); // Use entry Date, not CreatedDate
        }

        #endregion

        #region Property Change Notification

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Constructor

        public TimeEntry()
        {
            Id = Guid.Empty;
            Date = DateTime.Today;
            CreatedDate = DateTime.Now; // ? NEW: Set to now for new entries
            Hours = 0;
            Minutes = 0;
            Comments = string.Empty;
            ProjectId = Guid.Empty;
            QuoteId = Guid.Empty;
            TeamMemberId = Guid.Empty;
            IdGuid = Guid.Empty;
            ProjectName = string.Empty;
            ProjectNumber = string.Empty;
            QuoteName = string.Empty;
            QuoteNumber = string.Empty;
            ClientName = string.Empty;
            Category = TimeEntryCategory.Chargeable;
            Classification = TimeEntryClassification.Project;
        }

        #endregion

        #region Entity Conversion Methods

        // Convert from Dataverse Entity to TimeEntry model
        // Convert from Dataverse Entity to TimeEntry model
        public static TimeEntry FromEntity(Entity entity)
        {
            if (entity == null)
            {
                System.Diagnostics.Debug.WriteLine("? FromEntity: entity is null");
                return null;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"=== CONVERTING ENTITY {entity.Id} ===");

                // Log all available attributes
                System.Diagnostics.Debug.WriteLine($"Available attributes: {string.Join(", ", entity.Attributes.Keys)}");

                var timeEntry = new TimeEntry
                {
                    Id = entity.Id,
                    IdGuid = entity.Id,
                };

                // Date - with detailed logging
                if (entity.Contains("fwp_date"))
                {
                    var rawDate = entity.GetAttributeValue<DateTime>("fwp_date");
                    System.Diagnostics.Debug.WriteLine($"  fwp_date: {rawDate:yyyy-MM-dd HH:mm:ss} Kind:{rawDate.Kind}");
                    timeEntry.Date = rawDate;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("  ? fwp_date field MISSING!");
                    timeEntry.Date = DateTime.Today;
                }

                // Comments
                timeEntry.Comments = entity.GetAttributeValue<string>("fwp_notes") ?? string.Empty;
                System.Diagnostics.Debug.WriteLine($"  Comments: {timeEntry.Comments}");

                // CreatedDate
                if (entity.Contains("createdon"))
                {
                    timeEntry.CreatedDate = entity.GetAttributeValue<DateTime>("createdon");
                    System.Diagnostics.Debug.WriteLine($"  CreatedDate: {timeEntry.CreatedDate:yyyy-MM-dd}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("  ?? createdon field missing");
                    timeEntry.CreatedDate = DateTime.Now;
                }

                // Hours and Minutes conversion with error handling
                try
                {
                    var decimalHours = entity.GetAttributeValue<decimal>("fwp_decimalhours");
                    var storedMinutes = entity.GetAttributeValue<int>("fwp_minutes");

                    System.Diagnostics.Debug.WriteLine($"  Decimal hours: {decimalHours}, Stored minutes: {storedMinutes}");

                    timeEntry.Hours = Math.Floor(decimalHours);
                    var decimalPortion = decimalHours - timeEntry.Hours;
                    var calculatedMinutes = (int)Math.Round(decimalPortion * 60);
                    timeEntry.Minutes = Math.Max(0, Math.Min(59, calculatedMinutes));

                    System.Diagnostics.Debug.WriteLine($"  Converted to: {timeEntry.Hours}h {timeEntry.Minutes}m");
                }
                catch (Exception timeEx)
                {
                    System.Diagnostics.Debug.WriteLine($"  ? ERROR converting time: {timeEx.Message}");
                    timeEntry.Hours = 0;
                    timeEntry.Minutes = 0;
                }

                // Classification
                if (entity.Contains("fwp_classification"))
                {
                    try
                    {
                        var classificationValue = entity.GetAttributeValue<OptionSetValue>("fwp_classification");
                        if (classificationValue != null)
                        {
                            timeEntry.Classification = (TimeEntryClassification)classificationValue.Value;
                            System.Diagnostics.Debug.WriteLine($"  Classification: {timeEntry.Classification}");
                        }
                    }
                    catch (Exception classEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ? ERROR with classification: {classEx.Message}");
                    }
                }

                // Category
                if (entity.Contains("fwp_category"))
                {
                    try
                    {
                        var categoryValue = entity.GetAttributeValue<OptionSetValue>("fwp_category");
                        if (categoryValue != null)
                        {
                            timeEntry.Category = (TimeEntryCategory)categoryValue.Value;
                            System.Diagnostics.Debug.WriteLine($"  Category: {timeEntry.Category}");
                        }
                    }
                    catch (Exception catEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ? ERROR with category: {catEx.Message}");
                    }
                }

                // Project reference
                if (entity.Contains("fwp_project"))
                {
                    try
                    {
                        var projectRef = entity.GetAttributeValue<EntityReference>("fwp_project");
                        if (projectRef != null)
                        {
                            timeEntry.ProjectId = projectRef.Id;
                            System.Diagnostics.Debug.WriteLine($"  ProjectId: {timeEntry.ProjectId}");
                        }
                    }
                    catch (Exception projEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ? ERROR with project: {projEx.Message}");
                    }
                }

                // Quote reference
                if (entity.Contains("fwp_quote"))
                {
                    try
                    {
                        var quoteRef = entity.GetAttributeValue<EntityReference>("fwp_quote");
                        if (quoteRef != null)
                        {
                            timeEntry.QuoteId = quoteRef.Id;
                            System.Diagnostics.Debug.WriteLine($"  QuoteId: {timeEntry.QuoteId}");
                        }
                    }
                    catch (Exception quoteEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ? ERROR with quote: {quoteEx.Message}");
                    }
                }

                // Team member reference - CRITICAL
                if (entity.Contains("fwp_teammember"))
                {
                    try
                    {
                        var teamMemberRef = entity.GetAttributeValue<EntityReference>("fwp_teammember");
                        if (teamMemberRef != null)
                        {
                            timeEntry.TeamMemberId = teamMemberRef.Id;
                            System.Diagnostics.Debug.WriteLine($"  ? TeamMemberId: {timeEntry.TeamMemberId}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"  ?? fwp_teammember is null!");
                        }
                    }
                    catch (Exception teamEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ? ERROR with team member: {teamEx.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  ? fwp_teammember field MISSING!");
                }

                // Project info from linked entities
                if (entity.Contains("project.isc_projectnumbernew"))
                {
                    timeEntry.ProjectNumber = entity.GetAttributeValue<AliasedValue>("project.isc_projectnumbernew")?.Value?.ToString() ?? "";
                    System.Diagnostics.Debug.WriteLine($"  ProjectNumber: {timeEntry.ProjectNumber}");
                }

                if (entity.Contains("project.msdyn_subject"))
                {
                    timeEntry.ProjectName = entity.GetAttributeValue<AliasedValue>("project.msdyn_subject")?.Value?.ToString() ?? "";
                    System.Diagnostics.Debug.WriteLine($"  ProjectName: {timeEntry.ProjectName}");
                }

                // Quote info from linked entities
                if (entity.Contains("quote.quotenumber"))
                {
                    timeEntry.QuoteNumber = entity.GetAttributeValue<AliasedValue>("quote.quotenumber")?.Value?.ToString() ?? "";
                }

                if (entity.Contains("quote.name"))
                {
                    timeEntry.QuoteName = entity.GetAttributeValue<AliasedValue>("quote.name")?.Value?.ToString() ?? "";
                }

                // Client info
                if (entity.Contains("customer.name"))
                {
                    timeEntry.ClientName = entity.GetAttributeValue<AliasedValue>("customer.name")?.Value?.ToString() ?? "";
                }
                else if (entity.Contains("quotecustomer.name"))
                {
                    timeEntry.ClientName = entity.GetAttributeValue<AliasedValue>("quotecustomer.name")?.Value?.ToString() ?? "";
                }

                System.Diagnostics.Debug.WriteLine($"? Successfully converted entity {entity.Id}");
                return timeEntry;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"??? CRITICAL ERROR converting entity {entity.Id}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        // Convert from TimeEntry model to Dataverse Entity
        public Entity ToEntity()
        {
            var entity = new Entity("fwp_timeentry");

            System.Diagnostics.Debug.WriteLine($"");
            System.Diagnostics.Debug.WriteLine($"╔═══════════════════════════════════════════════════════════════════════════════╗");
            System.Diagnostics.Debug.WriteLine($"║ TIME ENTRY → DATAVERSE ENTITY CONVERSION DEBUG                                ║");
            System.Diagnostics.Debug.WriteLine($"╠═══════════════════════════════════════════════════════════════════════════════╣");

            if (Id != Guid.Empty)
            {
                entity.Id = Id;
                System.Diagnostics.Debug.WriteLine($"║ Entity ID: {Id}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"║ Entity ID: NEW ENTRY (will be generated)");
            }

            // Set basic fields
            entity["fwp_date"] = Date;
            System.Diagnostics.Debug.WriteLine($"║ fwp_date: {Date:yyyy-MM-dd HH:mm:ss} (Kind: {Date.Kind})");

            entity["fwp_notes"] = Comments ?? string.Empty;
            System.Diagnostics.Debug.WriteLine($"║ fwp_notes: \"{Comments ?? "(empty)"}\"");

            entity["fwp_minutes"] = Minutes;
            System.Diagnostics.Debug.WriteLine($"║ fwp_minutes: {Minutes}");

            // CRITICAL FIX: Set BOTH hour fields correctly
            entity["fwp_durationhours"] = Hours;      // Whole hours only (e.g., 1 for "1 hour 30 minutes")
            entity["fwp_decimalhours"] = TotalHours;  // Decimal hours (e.g., 1.5 for "1 hour 30 minutes")
            System.Diagnostics.Debug.WriteLine($"║ fwp_durationhours: {Hours} (whole hours component)");
            System.Diagnostics.Debug.WriteLine($"║ fwp_decimalhours: {TotalHours:F4} (total as decimal)");
            System.Diagnostics.Debug.WriteLine($"║ Display: {TotalTime}");

            // Set category
            entity["fwp_category"] = new OptionSetValue((int)Category);
            System.Diagnostics.Debug.WriteLine($"║ fwp_category: {(int)Category} ({CategoryName})");

            // Set classification
            entity["fwp_classification"] = new OptionSetValue((int)Classification);
            System.Diagnostics.Debug.WriteLine($"║ fwp_classification: {(int)Classification} ({ClassificationName})");

            // Set project or quote reference based on classification
            if (Classification == TimeEntryClassification.Project && ProjectId != Guid.Empty)
            {
                entity["fwp_project"] = new EntityReference("msdyn_project", ProjectId);
                entity["fwp_quote"] = null; // Clear quote reference
                System.Diagnostics.Debug.WriteLine($"║ fwp_project: {ProjectId}");
                System.Diagnostics.Debug.WriteLine($"║   → {ProjectNumber} - {ProjectName}");
                System.Diagnostics.Debug.WriteLine($"║ fwp_quote: NULL (cleared for project classification)");
            }
            else if (Classification == TimeEntryClassification.Quote && QuoteId != Guid.Empty)
            {
                entity["fwp_quote"] = new EntityReference("quote", QuoteId);
                entity["fwp_project"] = null; // Clear project reference
                System.Diagnostics.Debug.WriteLine($"║ fwp_quote: {QuoteId}");
                System.Diagnostics.Debug.WriteLine($"║   → {QuoteNumber} - {QuoteName}");
                System.Diagnostics.Debug.WriteLine($"║ fwp_project: NULL (cleared for quote classification)");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"║ fwp_project: NOT SET (ProjectId is empty or invalid)");
                System.Diagnostics.Debug.WriteLine($"║ fwp_quote: NOT SET (QuoteId is empty or invalid)");
            }

            // Set team member reference
            if (TeamMemberId != Guid.Empty)
            {
                entity["fwp_teammember"] = new EntityReference("systemuser", TeamMemberId);
                System.Diagnostics.Debug.WriteLine($"║ fwp_teammember: {TeamMemberId}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"║ fwp_teammember: NOT SET (TeamMemberId is empty)");
            }

            // Check if fwp_value exists in the entity (it shouldn't for time entries)
            if (entity.Contains("fwp_value"))
            {
                System.Diagnostics.Debug.WriteLine($"║ ⚠️  WARNING: fwp_value IS SET (unexpected for time entry)");
                System.Diagnostics.Debug.WriteLine($"║     Value: {entity["fwp_value"]}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"║ fwp_value: NOT SET (correct - this is a disbursement field)");
            }

            // Summary
            System.Diagnostics.Debug.WriteLine($"╠═══════════════════════════════════════════════════════════════════════════════╣");
            System.Diagnostics.Debug.WriteLine($"║ SUMMARY: {TotalTime} on {Date:yyyy-MM-dd} for {DisplayName}");
            System.Diagnostics.Debug.WriteLine($"╚═══════════════════════════════════════════════════════════════════════════════╝");
            System.Diagnostics.Debug.WriteLine($"");

            return entity;
        }

        #endregion
    }
}