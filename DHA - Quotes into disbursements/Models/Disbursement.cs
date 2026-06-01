using System;

namespace DHA.DSTC.WPF.Models
{
    public class Disbursement
    {
        public int Id { get; set; }
        public Guid IdGuid { get; set; } // Original Guid from Dataverse
        public DateTime Date { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public int ProjectId { get; set; }
        public Guid ProjectGuid { get; set; } // Original Guid 
        public int DisbursementTypeId { get; set; }
        public Guid DisbursementTypeGuid { get; set; } // Original Guid from Dataverse disbursement type table
        public int TeamMemberId { get; set; }
        public Guid TeamMemberGuid { get; set; } // Original Guid
        public bool BillableToClient { get; set; }

        // New unit-based properties
        public decimal Units { get; set; } // Number of units (e.g., miles)
        public decimal UnitCharge { get; set; } // Cost per unit

        // Navigation properties - useful when using Entity Framework
        public string ProjectName { get; set; }
        public string ProjectNumber { get; set; }
        public string DisbursementTypeName { get; set; }
        public string TeamMemberName { get; set; }

        // NEW: Quote-related properties
        public Guid QuoteId { get; set; } = Guid.Empty;
        public string QuoteName { get; set; } = "";
        public string QuoteNumber { get; set; } = "";
        public DisbursementClassification Classification { get; set; } = DisbursementClassification.Project;

        // Navigation properties for client info
        public string ClientName { get; set; }

        // MISSING PROPERTY: Helper property for data binding - returns human-readable classification name
        // Enhanced ClassificationName property with setter for WPF binding compatibility
        public string ClassificationName
        {
            get
            {
                switch (Classification)
                {
                    case DisbursementClassification.Project:
                        return "Project";
                    case DisbursementClassification.Quote:
                        return "Quote";
                    default:
                        return "Unknown";
                }
            }
            set
            {
                // Convert string back to enum for two-way binding support
                switch (value?.ToUpperInvariant())
                {
                    case "PROJECT":
                        Classification = DisbursementClassification.Project;
                        break;
                    case "QUOTE":
                        Classification = DisbursementClassification.Quote;
                        break;
                    default:
                        // Don't change classification for invalid values
                        break;
                }
            }
        }

        // Helper property to determine if this disbursement uses units
        public bool IsUnitBased => UnitCharge > 0;

        // Display name that shows either project or quote information
        public string DisplayName
        {
            get
            {
                if (Classification == DisbursementClassification.Quote && !string.IsNullOrEmpty(QuoteName))
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

        public Disbursement()
        {
            Date = DateTime.Today;
            BillableToClient = true;
            Classification = DisbursementClassification.Project; // Default to project
        }

        public override string ToString()
        {
            return $"{Date.ToShortDateString()} - {Description} - {Amount:C}";
        }
    }

    // NEW: Classification enum to match TimeEntry pattern
    public enum DisbursementClassification
    {
        Project = 800470000,
        Quote = 800470001
    }

    public class DisbursementType
    {
        public int Id { get; set; }
        public Guid IdGuid { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal UnitCharge { get; set; } // New property

        // Helper property to determine if this is a unit-based disbursement
        public bool IsUnitBased => UnitCharge > 0;

        public override string ToString()
        {
            return Name;
        }
    }
}