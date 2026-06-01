using Microsoft.Xrm.Sdk;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;

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
            set { _quoteName = value; OnPropertyChanged(); }
        }

        public string QuoteNumber
        {
            get => _quoteNumber;
            set { _quoteNumber = value; OnPropertyChanged(); }
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

        // Existing properties
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

        // Lock-related properties
        public DateTime LockDate
        {
            get
            {
                // Calculate lock date: 12 noon on the Monday following the work date
                var daysUntilMonday = ((int)DayOfWeek.Monday - (int)Date.DayOfWeek + 7) % 7;
                if (daysUntilMonday == 0 && Date.DayOfWeek != DayOfWeek.Monday)
                {
                    daysUntilMonday = 7;
                }
                return Date.Date.AddDays(daysUntilMonday).AddHours(12);
            }
        }

        public bool IsLocked => DateTime.Now > LockDate;
        public bool IsEditable => !IsLocked;

        // Property change notification
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Constructor
        public TimeEntry()
        {
            Id = Guid.Empty;
            Date = DateTime.Today;
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

        // Convert from Dataverse Entity to TimeEntry model
        public static TimeEntry FromEntity(Entity entity)
        {
            if (entity == null)
                return null;

            try
            {
                var timeEntry = new TimeEntry
                {
                    Id = entity.Id,
                    IdGuid = entity.Id,
                    Date = entity.GetAttributeValue<DateTime>("fwp_date"),
                    Hours = entity.GetAttributeValue<decimal>("fwp_decimalhours"),
                    Minutes = entity.GetAttributeValue<int>("fwp_minutes"),
                    Comments = entity.GetAttributeValue<string>("fwp_notes") ?? string.Empty
                };

                // Get classification
                if (entity.Contains("fwp_classification"))
                {
                    var classificationValue = entity.GetAttributeValue<OptionSetValue>("fwp_classification");
                    if (classificationValue != null)
                    {
                        timeEntry.Classification = (TimeEntryClassification)classificationValue.Value;
                    }
                }

                // Get category
                if (entity.Contains("fwp_category"))
                {
                    var categoryValue = entity.GetAttributeValue<OptionSetValue>("fwp_category");
                    if (categoryValue != null)
                    {
                        timeEntry.Category = (TimeEntryCategory)categoryValue.Value;
                    }
                }

                // Get project reference
                if (entity.Contains("fwp_project"))
                {
                    var projectRef = entity.GetAttributeValue<EntityReference>("fwp_project");
                    if (projectRef != null)
                    {
                        timeEntry.ProjectId = projectRef.Id;
                    }
                }

                // Get quote reference
                if (entity.Contains("fwp_quote"))
                {
                    var quoteRef = entity.GetAttributeValue<EntityReference>("fwp_quote");
                    if (quoteRef != null)
                    {
                        timeEntry.QuoteId = quoteRef.Id;
                    }
                }

                // Get team member reference
                if (entity.Contains("fwp_teammember"))
                {
                    var teamMemberRef = entity.GetAttributeValue<EntityReference>("fwp_teammember");
                    if (teamMemberRef != null)
                    {
                        timeEntry.TeamMemberId = teamMemberRef.Id;
                    }
                }

                // Get project information from linked entities
                if (entity.Contains("project.isc_projectnumbernew"))
                {
                    timeEntry.ProjectNumber = entity.GetAttributeValue<AliasedValue>("project.isc_projectnumbernew")?.Value?.ToString() ?? "";
                }

                if (entity.Contains("project.msdyn_subject"))
                {
                    timeEntry.ProjectName = entity.GetAttributeValue<AliasedValue>("project.msdyn_subject")?.Value?.ToString() ?? "";
                }

                // Get quote information from linked entities
                if (entity.Contains("quote.quotenumber"))
                {
                    timeEntry.QuoteNumber = entity.GetAttributeValue<AliasedValue>("quote.quotenumber")?.Value?.ToString() ?? "";
                }

                if (entity.Contains("quote.name"))
                {
                    timeEntry.QuoteName = entity.GetAttributeValue<AliasedValue>("quote.name")?.Value?.ToString() ?? "";
                }

                // Get client information
                if (entity.Contains("customer.name"))
                {
                    timeEntry.ClientName = entity.GetAttributeValue<AliasedValue>("customer.name")?.Value?.ToString() ?? "";
                }

                return timeEntry;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting entity to TimeEntry: {ex.Message}");
                return null;
            }
        }

        // Convert from TimeEntry model to Dataverse Entity
        public Entity ToEntity()
        {
            var entity = new Entity("fwp_timeentry");

            if (Id != Guid.Empty)
                entity.Id = Id;

            entity["fwp_date"] = Date;
            entity["fwp_decimalhours"] = Hours;
            entity["fwp_minutes"] = Minutes;
            entity["fwp_notes"] = Comments ?? string.Empty;

            // Set category
            entity["fwp_category"] = new OptionSetValue((int)Category);

            // Set classification
            entity["fwp_classification"] = new OptionSetValue((int)Classification);

            // Set project or quote reference based on classification
            if (Classification == TimeEntryClassification.Project && ProjectId != Guid.Empty)
            {
                entity["fwp_project"] = new EntityReference("msdyn_project", ProjectId);
                entity["fwp_quote"] = null;
            }
            else if (Classification == TimeEntryClassification.Quote && QuoteId != Guid.Empty)
            {
                entity["fwp_quote"] = new EntityReference("quote", QuoteId);
                entity["fwp_project"] = null;
            }

            // Set team member reference
            if (TeamMemberId != Guid.Empty)
            {
                entity["fwp_teammember"] = new EntityReference("systemuser", TeamMemberId);
            }

            return entity;
        }
    }
}