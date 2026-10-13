using System;
using Microsoft.Xrm.Sdk;

namespace DHA.DSTC.WPF.Models
{
    // Option values for fwp_leaverequest.fwp_status - must match DHA.Leave.Plugins LeaveStatus
    public enum LeaveRequestStatus
    {
        Pending = 800470000,
        Approved = 800470001,
        Rejected = 800470002,
        CancellationRequested = 800470003,
        Cancelled = 800470004
    }

    public class LeaveType
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public bool DeductsFromEntitlement { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>A leave request (fwp_leaverequest) - phase B of DHA-9, leave booked in the desktop app.</summary>
    public class LeaveRequest
    {
        public Guid Id { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public DateTime? RequestedOn { get; set; }
        public Guid? LeaveTypeId { get; set; }
        public string LeaveTypeName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string StartTime { get; set; }   // "HH:mm", null = start of working day
        public string EndTime { get; set; }     // "HH:mm", null = end of working day
        public decimal Hours { get; set; }
        public LeaveRequestStatus Status { get; set; }
        public string ApproverName { get; set; }
        public string RequesterComments { get; set; }
        public string ApproverComments { get; set; }

        public string DatesText
        {
            get
            {
                string start = StartDate.ToString("ddd dd/MM/yyyy") + (StartTime != null ? " " + StartTime : "");
                string end = EndDate.ToString("ddd dd/MM/yyyy") + (EndTime != null ? " " + EndTime : "");
                return StartDate.Date == EndDate.Date && StartTime == null && EndTime == null ? start : $"{start} – {end}";
            }
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case LeaveRequestStatus.CancellationRequested: return "Cancellation requested";
                    default: return Status.ToString();
                }
            }
        }

        public bool CanCancel => Status == LeaveRequestStatus.Pending || Status == LeaveRequestStatus.Approved;

        public bool IsCancellationRequest => Status == LeaveRequestStatus.CancellationRequested;

        public static LeaveRequest FromEntity(Entity e)
        {
            var type = e.GetAttributeValue<EntityReference>("fwp_leavetype");
            var employee = e.GetAttributeValue<EntityReference>("fwp_employee");
            return new LeaveRequest
            {
                Id = e.Id,
                EmployeeId = employee?.Id ?? Guid.Empty,
                EmployeeName = employee?.Name ?? "",
                RequestedOn = e.GetAttributeValue<DateTime?>("createdon")?.ToLocalTime(),
                LeaveTypeId = type?.Id,
                LeaveTypeName = type?.Name ?? "",
                StartDate = e.GetAttributeValue<DateTime>("fwp_startdate"),
                EndDate = e.GetAttributeValue<DateTime?>("fwp_enddate") ?? e.GetAttributeValue<DateTime>("fwp_startdate"),
                StartTime = e.GetAttributeValue<string>("fwp_starttime"),
                EndTime = e.GetAttributeValue<string>("fwp_endtime"),
                Hours = e.GetAttributeValue<decimal?>("fwp_hours") ?? 0m,
                Status = (LeaveRequestStatus)(e.GetAttributeValue<OptionSetValue>("fwp_status")?.Value ?? (int)LeaveRequestStatus.Pending),
                ApproverName = e.GetAttributeValue<EntityReference>("fwp_approver")?.Name ?? "",
                RequesterComments = e.GetAttributeValue<string>("fwp_requestercomments"),
                ApproverComments = e.GetAttributeValue<string>("fwp_approvercomments")
            };
        }

        public Entity ToEntity()
        {
            var e = new Entity("fwp_leaverequest");
            if (Id != Guid.Empty) e.Id = Id;
            e["fwp_employee"] = new EntityReference("systemuser", EmployeeId);
            if (LeaveTypeId.HasValue) e["fwp_leavetype"] = new EntityReference("fwp_leavetype", LeaveTypeId.Value);
            e["fwp_startdate"] = DateTime.SpecifyKind(StartDate.Date, DateTimeKind.Unspecified);
            e["fwp_enddate"] = DateTime.SpecifyKind(EndDate.Date, DateTimeKind.Unspecified);
            e["fwp_starttime"] = StartTime;
            e["fwp_endtime"] = EndTime;
            e["fwp_requestercomments"] = RequesterComments;
            // fwp_hours, fwp_status, fwp_approver are set server-side by LeaveRequestPlugin
            return e;
        }
    }

    /// <summary>Leave balance for the current leave year, in hours (fwp_leaveentitlement + requests).</summary>
    public class LeaveBalance
    {
        public DateTime YearStart { get; set; }
        public decimal EntitlementHours { get; set; }
        public decimal TakenHours { get; set; }
        public decimal BookedHours { get; set; }
        public decimal PendingHours { get; set; }
        public decimal RemainingHours => EntitlementHours - TakenHours - BookedHours;
    }
}
