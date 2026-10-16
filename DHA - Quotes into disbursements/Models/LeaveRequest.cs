using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>False for Working From Home: approved like leave, never on the timesheet.</summary>
        public bool IsAbsence { get; set; } = true;
        /// <summary>Booked with start/end times (e.g. Medical) rather than full or half days.</summary>
        public bool BookedInHours { get; set; }

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
        /// <summary>Length in days (entitlement is counted in days), worked out server-side.</summary>
        public decimal Days { get; set; }
        public LeaveRequestStatus Status { get; set; }
        public string ApproverName { get; set; }
        public string AdditionalApproverName { get; set; }
        public string CancellationComments { get; set; }
        public DateTime? CancellationRequestedOn { get; set; }
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

        /// <summary>
        /// A pending request is withdrawn straight away (nothing was approved, the approver is told); approved leave needs the
        /// approver to confirm. Valto also sent pending requests that had already started to the approver - the server now
        /// refuses that, because declining the cancellation would approve leave nobody approved.
        /// </summary>
        public bool CancelsInstantly => Status == LeaveRequestStatus.Pending;

        public bool IsUpcoming => EndDate.Date >= DateTime.Today;

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
                Days = e.GetAttributeValue<decimal?>("fwp_days") ?? 0m,
                Status = (LeaveRequestStatus)(e.GetAttributeValue<OptionSetValue>("fwp_status")?.Value ?? (int)LeaveRequestStatus.Pending),
                ApproverName = e.GetAttributeValue<EntityReference>("fwp_approver")?.Name ?? "",
                AdditionalApproverName = e.GetAttributeValue<EntityReference>("fwp_additionalapprover")?.Name ?? "",
                CancellationComments = e.GetAttributeValue<string>("fwp_cancellationcomments"),
                CancellationRequestedOn = e.GetAttributeValue<DateTime?>("fwp_cancellationrequestedon")?.ToLocalTime(),
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

    /// <summary>Someone whose leave you can see in the team views.</summary>
    public class LeavePerson
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>
    /// What the signed-in user approves (parity plan M1). Leave administrators (HR) see everyone; line managers and
    /// additional approvers see the people whose colleague configuration names them.
    /// </summary>
    public class ApproverScope
    {
        public bool IsLeaveAdmin { get; set; }
        /// <summary>The people this user approves directly (as approver or additional approver).</summary>
        public List<LeavePerson> MyPeople { get; set; } = new List<LeavePerson>();
        public bool CanApprove => IsLeaveAdmin || MyPeople.Count > 0;
        public bool IsMine(Guid employeeId) => MyPeople.Any(p => p.Id == employeeId);
    }

    /// <summary>
    /// Leave balance for one leave year (1 Jan - 31 Dec), in DAYS. As in Valto, pending requests count as taken
    /// (HR decision 15 Oct 2026); PendingDays says how much of that is still waiting for approval.
    /// </summary>
    public class LeaveBalance
    {
        public DateTime YearStart { get; set; }
        /// <summary>Base + carried forward + adjustment.</summary>
        public decimal EntitlementDays { get; set; }
        public decimal BaseDays { get; set; }
        public decimal CarriedForwardDays { get; set; }
        public decimal AdjustmentDays { get; set; }
        public decimal CarriedToNextYearDays { get; set; }
        /// <summary>Approved leave already in the past.</summary>
        public decimal TakenDays { get; set; }
        /// <summary>Approved future leave plus pending requests.</summary>
        public decimal BookedDays { get; set; }
        public decimal PendingDays { get; set; }
        public decimal RemainingDays => EntitlementDays - TakenDays - BookedDays;
        public bool HasEntitlement => YearStart != default(DateTime);
    }
}
