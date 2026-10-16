using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.Leave.Plugins
{
    /// <summary>Option values for fwp_leaverequest.fwp_status - must match DHA.DSTC.WPF Models/LeaveRequest.cs.</summary>
    public static class LeaveStatus
    {
        public const int Pending = 800470000;
        public const int Approved = 800470001;
        public const int Rejected = 800470002;
        public const int CancellationRequested = 800470003;
        public const int Cancelled = 800470004;
    }

    /// <summary>
    /// Values of fwp_leaverequest.fwp_lastevent - what just happened to a request. The "Leave - Notifications"
    /// flow triggers on this column; Dataverse flow triggers can't see the previous status, so without it an
    /// approval and a declined cancellation (both "now Approved") would be indistinguishable.
    /// </summary>
    public static class LeaveEvent
    {
        public const string Requested = "Requested";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Withdrawn = "Withdrawn";                       // employee cancelled a pending request
        public const string CancellationRequested = "CancellationRequested";
        public const string CancellationApproved = "CancellationApproved";
        public const string CancellationDeclined = "CancellationDeclined";
        public const string CancelledByHR = "CancelledByHR";               // cancelled directly by an approver or HR (name kept for the flow)

        /// <param name="byEmployee">The change was made by the person whose leave it is (false when an approver or HR cancels on their behalf).</param>
        public static string For(int? oldStatus, int newStatus, bool byEmployee = true)
        {
            switch (newStatus)
            {
                case LeaveStatus.Approved:
                    return oldStatus == LeaveStatus.CancellationRequested ? CancellationDeclined : Approved;
                case LeaveStatus.Rejected:
                    return Rejected;
                case LeaveStatus.CancellationRequested:
                    return CancellationRequested;
                case LeaveStatus.Cancelled:
                    return oldStatus == LeaveStatus.CancellationRequested ? CancellationApproved
                         : oldStatus == LeaveStatus.Approved || !byEmployee ? CancelledByHR
                         : Withdrawn;
                default:
                    return Requested;
            }
        }

        /// <summary>
        /// Only approved leave can go to the approver for cancellation. A pending request has nothing approved to undo, so it is
        /// withdrawn instead; letting it through would put unapproved leave on the timesheet, and "declining" the cancellation
        /// would approve it.
        /// </summary>
        public static bool CanRequestCancellation(int? oldStatus) =>
            oldStatus == LeaveStatus.Approved || oldStatus == LeaveStatus.CancellationRequested;
    }

    /// <summary>
    /// Server-side rules for fwp_leaverequest (phase B: leave booked in the desktop app).
    ///
    /// REGISTER on fwp_leaverequest, synchronous:
    ///   Create, Pre-operation   - work out hours, default the approver, reject overlaps
    ///   Update, Pre-operation   - same, filtering attributes fwp_startdate, fwp_enddate, fwp_starttime, fwp_endtime, fwp_status,
    ///                             fwp_approver, fwp_additionalapprover, fwp_leavetype
    ///                             Pre Image "PreImage": fwp_employee, fwp_approver, fwp_additionalapprover, fwp_leavetype, fwp_startdate,
    ///                             fwp_enddate, fwp_starttime, fwp_endtime, fwp_status, fwp_legacyid
    ///   Create, Post-operation  - assign to the employee (when booked on their behalf), share with the approvers
    ///   Update, Post-operation  - status/date changes drive time entries; same filtering attributes and Pre Image
    ///   Delete, Pre-validation  - approved leave can't be deleted (DHA-10: records are never deleted - cancel it instead)
    ///
    /// Time entries are written through LeaveTimeEntryService - the same code the phase A Custom APIs use -
    /// so a request migrated from SharePoint (fwp_legacyid "LR-123") keeps the entries phase A created.
    /// </summary>
    public class LeaveRequestPlugin : IPlugin
    {
        private const int PreOperation = 20;
        private const int PostOperation = 40;

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            if (context.MessageName == "Delete")
            {
                BlockDeletingBookedLeave(context, serviceProvider);
                return;
            }
            if (!context.InputParameters.TryGetValue("Target", out var boxed) || !(boxed is Entity target)) return;
            if (target.LogicalName != "fwp_leaverequest") return;

            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            // Approvers and employees can't write each other's time entries, so the plug-in works as SYSTEM.
            // Access to the request itself has already been checked by the platform for the calling user.
            var svc = factory.CreateOrganizationService(null);

            var pre = context.PreEntityImages.Contains("PreImage") ? context.PreEntityImages["PreImage"] : null;
            T Get<T>(string attr) => target.Contains(attr) ? target.GetAttributeValue<T>(attr) : pre != null ? pre.GetAttributeValue<T>(attr) : default(T);

            try
            {
                if (context.Stage == PreOperation)
                    PreOperationRules(context, target, svc, trace, Get<EntityReference>, Get<DateTime?>, Get<string>, Get<OptionSetValue>);
                else if (context.Stage == PostOperation)
                    PostOperationRules(context, target, pre, svc, trace, Get<EntityReference>, Get<DateTime?>, Get<string>, Get<OptionSetValue>);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                trace.Trace("LeaveRequestPlugin error: {0}", ex);
                throw new InvalidPluginExecutionException("Leave request could not be saved: " + ex.Message, ex);
            }
        }

        private static void PreOperationRules(IPluginExecutionContext context, Entity target, IOrganizationService svc, ITracingService trace,
            Func<string, EntityReference> getRef, Func<string, DateTime?> getDate, Func<string, string> getText, Func<string, OptionSetValue> getOption)
        {
            var employee = getRef("fwp_employee");
            if (employee == null && context.MessageName == "Create")
            {
                employee = new EntityReference("systemuser", context.InitiatingUserId);
                target["fwp_employee"] = employee;
            }

            var start = getDate("fwp_startdate");
            var end = getDate("fwp_enddate") ?? start;
            if (employee == null || start == null)
                throw new InvalidPluginExecutionException("A leave request needs an employee and a start date.");
            // Booking for someone else is for HR and the "Leave - Book on behalf" team (Valto's "Book on behalf of users" list, A9)
            if (context.MessageName == "Create" && employee.Id != context.InitiatingUserId
                && !LeaveAccess.CanBookOnBehalf(svc, context.InitiatingUserId))
                throw new InvalidPluginExecutionException(
                    "You can only book leave for someone else if you're in HR or the \"" + LeaveAccess.BookOnBehalfTeam + "\" team.");

            // A one-day request may come without an end date; store it, or date-range queries (overlaps, who's off) miss it
            if (getDate("fwp_enddate") == null)
                target["fwp_enddate"] = start.Value;
            if (end < start)
                throw new InvalidPluginExecutionException("The leave end date is before the start date.");
            if (LeaveDayCalculator.SpansLeaveYears(start.Value, end.Value))
                throw new InvalidPluginExecutionException(
                    "Leave can't run across 31 December into the new leave year. Please book it as two requests, one each side of 1 January.");

            if (context.MessageName == "Create")
            {
                if (!target.Contains("fwp_status"))
                    target["fwp_status"] = new OptionSetValue(LeaveStatus.Pending);
                target["fwp_lastevent"] = LeaveEvent.Requested;
                if (getRef("fwp_approver") == null || getRef("fwp_additionalapprover") == null)
                {
                    var (approver, additional) = DefaultApprovers(svc, employee.Id);
                    if (getRef("fwp_approver") == null && approver != null) target["fwp_approver"] = approver;
                    if (getRef("fwp_additionalapprover") == null && additional != null) target["fwp_additionalapprover"] = additional;
                }
                if (!target.Contains("fwp_bookedby"))
                    target["fwp_bookedby"] = new EntityReference("systemuser", context.InitiatingUserId);
                if (string.IsNullOrWhiteSpace(target.GetAttributeValue<string>("fwp_name")))
                {
                    var name = svc.Retrieve("systemuser", employee.Id, new ColumnSet("fullname")).GetAttributeValue<string>("fullname");
                    target["fwp_name"] = $"{name} - {start.Value:dd/MM/yyyy}" + (end.Value.Date != start.Value.Date ? $" to {end.Value:dd/MM/yyyy}" : "");
                }
            }

            // Hours and days are always worked out here, so the desktop app's preview can't drift from what's stored
            var leave = new LeaveTimeEntryService(svc, trace);
            var settings = LeaveSettings.Load(svc);
            var pattern = leave.LoadWorkingPattern(employee.Id, settings.LeaveDayHours);
            var days = LeaveDayCalculator.Calculate(start.Value, end.Value,
                LeaveDayCalculator.ParseTime(getText("fwp_starttime"), false),
                LeaveDayCalculator.ParseTime(getText("fwp_endtime"), true),
                pattern, leave.LoadBankHolidays(start.Value, end.Value));
            target["fwp_hours"] = days.Sum(d => d.Hours);
            target["fwp_days"] = LeaveDayCalculator.DaysFor(days, pattern);

            int status = getOption("fwp_status")?.Value ?? LeaveStatus.Pending;
            // Only absences clash with each other: working from home alongside an appointment is fine (as in Valto)
            if (status != LeaveStatus.Rejected && status != LeaveStatus.Cancelled && IsAbsence(svc, getRef("fwp_leavetype")))
                RejectOverlaps(svc, target.Id, employee.Id, start.Value, end.Value);

            // Record what happened for the notifications flow (only when the status actually changes)
            if (context.MessageName == "Update" && target.Contains("fwp_status"))
            {
                var pre = context.PreEntityImages.Contains("PreImage") ? context.PreEntityImages["PreImage"] : null;
                int? oldStatus = pre?.GetAttributeValue<OptionSetValue>("fwp_status")?.Value;
                if (status == LeaveStatus.CancellationRequested && !LeaveEvent.CanRequestCancellation(oldStatus))
                    throw new InvalidPluginExecutionException(
                        "Only approved leave needs the approver to confirm a cancellation. Cancel a pending request instead.");
                if (oldStatus != status)
                    target["fwp_lastevent"] = LeaveEvent.For(oldStatus, status, context.InitiatingUserId == employee.Id);
                if (status == LeaveStatus.CancellationRequested && oldStatus != status)
                    target["fwp_cancellationrequestedon"] = DateTime.UtcNow;
            }
        }

        private static void PostOperationRules(IPluginExecutionContext context, Entity target, Entity pre, IOrganizationService svc, ITracingService trace,
            Func<string, EntityReference> getRef, Func<string, DateTime?> getDate, Func<string, string> getText, Func<string, OptionSetValue> getOption)
        {
            // On Create the new id is only guaranteed in the output parameters
            var requestId = target.Id != Guid.Empty ? target.Id : (Guid)context.OutputParameters["id"];

            // Booked on someone's behalf: the request belongs to the employee, so they see it under "own" (DHA-1).
            // Dynamics only allows assigning to someone who can read leave requests, and a failed call can't be caught
            // inside the save - so check first. Without the privilege (no DHA-1) the booking stays with whoever made it.
            var employee = getRef("fwp_employee");
            if (context.MessageName == "Create" && employee != null && context.InitiatingUserId != employee.Id)
            {
                if (LeaveAccess.HasPrivilege(svc, employee.Id, "prvReadfwp_LeaveRequest"))
                    svc.Update(new Entity("fwp_leaverequest", requestId) { ["ownerid"] = employee });
                else
                    trace.Trace($"{employee.Id} can't read leave requests (no DHA-1?) - request {requestId} left with the person who booked it.");
            }

            foreach (var field in new[] { "fwp_approver", "fwp_additionalapprover" })
            {
                var approver = getRef(field);
                if (approver != null && (context.MessageName == "Create" || target.Contains(field)))
                {
                    svc.Execute(new GrantAccessRequest
                    {
                        Target = new EntityReference("fwp_leaverequest", requestId),
                        PrincipalAccess = new PrincipalAccess { Principal = approver, AccessMask = AccessRights.ReadAccess | AccessRights.WriteAccess }
                    });
                }
            }

            int status = getOption("fwp_status")?.Value ?? LeaveStatus.Pending;
            int? oldStatus = pre?.GetAttributeValue<OptionSetValue>("fwp_status")?.Value;
            bool datesChanged = target.Contains("fwp_startdate") || target.Contains("fwp_enddate") || target.Contains("fwp_starttime")
                                || target.Contains("fwp_endtime") || target.Contains("fwp_leavetype");
            if (status == oldStatus && !datesChanged)
                return;

            var legacy = getText("fwp_legacyid");
            var sourceKey = !string.IsNullOrEmpty(legacy) ? legacy : "LV-" + requestId.ToString("N");
            var leave = new LeaveTimeEntryService(svc, trace);

            // Approved ABSENCE stays as time entries until it is fully cancelled; a pending cancellation keeps them.
            // Working from home is approved the same way but never goes on the timesheet.
            bool onTimesheet = (status == LeaveStatus.Approved || status == LeaveStatus.CancellationRequested)
                               && IsAbsence(svc, getRef("fwp_leavetype"));
            if (onTimesheet)
            {
                var end = getDate("fwp_enddate") ?? getDate("fwp_startdate").Value;
                var endTime = LeaveDayCalculator.ParseTime(getText("fwp_endtime"), true);
                leave.RecordLeave(getRef("fwp_employee").Id, getDate("fwp_startdate").Value, end,
                    LeaveDayCalculator.ParseTime(getText("fwp_starttime"), false), endTime,
                    LeaveTimeEntryService.KindLeave, sourceKey, leaveRequestId: requestId);
            }
            else
            {
                leave.RemoveLeave(sourceKey);
            }
        }

        private static void BlockDeletingBookedLeave(IPluginExecutionContext context, IServiceProvider serviceProvider)
        {
            if (!(context.InputParameters["Target"] is EntityReference target) || target.LogicalName != "fwp_leaverequest")
                return;

            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var request = factory.CreateOrganizationService(null).Retrieve("fwp_leaverequest", target.Id, new ColumnSet("fwp_status"));
            int? status = request.GetAttributeValue<OptionSetValue>("fwp_status")?.Value;
            if (status == LeaveStatus.Approved || status == LeaveStatus.CancellationRequested)
                throw new InvalidPluginExecutionException(
                    "Approved leave can't be deleted because it is on the person's timesheet. Cancel it instead - " +
                    "its time entries will be deactivated and the request kept for the record.");
        }

        /// <summary>Approver and additional approver from colleague configuration; approver falls back to the user's manager.</summary>
        private static (EntityReference approver, EntityReference additional) DefaultApprovers(IOrganizationService svc, Guid employeeId)
        {
            var q = new QueryExpression("fwp_colleagueconfiguration")
            {
                ColumnSet = new ColumnSet("fwp_leaveapprover", "fwp_additionalleaveapprover"),
                TopCount = 1
            };
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var cfg = svc.RetrieveMultiple(q).Entities.FirstOrDefault();
            var approver = cfg?.GetAttributeValue<EntityReference>("fwp_leaveapprover")
                           ?? svc.Retrieve("systemuser", employeeId, new ColumnSet("parentsystemuserid")).GetAttributeValue<EntityReference>("parentsystemuserid");
            return (approver, cfg?.GetAttributeValue<EntityReference>("fwp_additionalleaveapprover"));
        }

        /// <summary>Requests with no type are treated as absence. Working From Home has fwp_isabsence = No.</summary>
        private static bool IsAbsence(IOrganizationService svc, EntityReference leaveType)
        {
            if (leaveType == null)
                return true;
            return svc.Retrieve("fwp_leavetype", leaveType.Id, new ColumnSet("fwp_isabsence")).GetAttributeValue<bool?>("fwp_isabsence") ?? true;
        }

        private static void RejectOverlaps(IOrganizationService svc, Guid requestId, Guid employeeId, DateTime start, DateTime end)
        {
            var q = new QueryExpression("fwp_leaverequest") { ColumnSet = new ColumnSet("fwp_startdate", "fwp_enddate"), TopCount = 1 };
            // only other absences count (or requests with no type)
            var type = q.AddLink("fwp_leavetype", "fwp_leavetype", "fwp_leavetypeid", JoinOperator.LeftOuter);
            type.EntityAlias = "t";
            var absence = new FilterExpression(LogicalOperator.Or);
            absence.AddCondition("t", "fwp_isabsence", ConditionOperator.Equal, true);
            absence.AddCondition("t", "fwp_isabsence", ConditionOperator.Null);
            q.Criteria.AddFilter(absence);
            q.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("fwp_status", ConditionOperator.NotIn, LeaveStatus.Rejected, LeaveStatus.Cancelled);
            q.Criteria.AddCondition("fwp_startdate", ConditionOperator.OnOrBefore, end.Date);
            q.Criteria.AddCondition("fwp_enddate", ConditionOperator.OnOrAfter, start.Date);
            if (requestId != Guid.Empty)
                q.Criteria.AddCondition("fwp_leaverequestid", ConditionOperator.NotEqual, requestId);

            var clash = svc.RetrieveMultiple(q).Entities.FirstOrDefault();
            if (clash != null)
                throw new InvalidPluginExecutionException(
                    $"You already have leave booked from {clash.GetAttributeValue<DateTime>("fwp_startdate"):dd/MM/yyyy} to {clash.GetAttributeValue<DateTime>("fwp_enddate"):dd/MM/yyyy} that overlaps these dates.");
        }
    }
}
