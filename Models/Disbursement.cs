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
        public int TeamMemberId { get; set; }
        public Guid TeamMemberGuid { get; set; } // Original Guid
        public bool BillableToClient { get; set; }

        // Navigation properties - useful when using Entity Framework
        public string ProjectName { get; set; }
        public string DisbursementTypeName { get; set; }
        public string TeamMemberName { get; set; }

        public Disbursement()
        {
            Date = DateTime.Today;
            BillableToClient = true;
        }

        public override string ToString()
        {
            return $"{Date.ToShortDateString()} - {Description} - {Amount:C}";
        }
    }

    public class DisbursementType
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}