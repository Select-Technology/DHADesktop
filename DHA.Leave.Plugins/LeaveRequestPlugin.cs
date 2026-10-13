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
    /// Server-side rules for fwp_leaverequest (phase B: leave booked in the desktop app).
    ///
    /// REGISTER on fwp_leaverequest, synchronous:
    ///   Create, Pre-operation   - work out hours, default the approver, reject overlaps
    ///   Update, Pre-operation   - same, filtering attributes fwp_startdate, fwp_enddate, fwp_starttime, fwp_endtime, fwp_status
    ///                             Pre Image "PreImage": fwp_employee, fwp_approver, fwp_startdate, fwp_enddate, fwp_starttime, fwp_endtime, fwp_status, fwp_legacyid
    ///   Create, Post-operation  - share with the approver
    ///   Update, Post-operation  - status/date changes drive time entries; same filtering attributes and Pre Image
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
            if (end < start)
                throw new InvalidPluginExecutionException("The leave end date is before the start date.");

            if (context.MessageName == "Create")
            {
                if (!target.Contains("fwp_status"))
                    target["fwp_status"] = new OptionSetValue(LeaveStatus.Pending);
                if (getRef("fwp_approver") == null)
                {
                    var approver = DefaultApprover(svc, employee.Id);
                    if (approver != null) target["fwp_approver"] = approver;
                }
                if (!target.Contains("fwp_bookedby"))
                    target["fwp_bookedby"] = new EntityReference("systemuser", context.InitiatingUserId);
            }

            // Hours are always worked out here, so the desktop app's preview can't drift from what's stored
            var leave = new LeaveTimeEntryService(svc, trace);
            var settings = LeaveSettings.Load(svc);
            var days = LeaveDayCalculator.Calculate(start.Value, end.Value,
                LeaveDayCalculator.ParseTime(getText("fwp_starttime"), false),
                LeaveDayCalculator.ParseTime(getText("fwp_endtime"), true),
                leave.LoadWorkingPattern(employee.Id, settings.DefaultDayHours),
                leave.LoadBankHolidays(start.Value, end.Value));
            target["fwp_hours"] = days.Sum(d => d.Hours);

            int status = getOption("fwp_status")?.Value ?? LeaveStatus.Pending;
            if (status != LeaveStatus.Rejected && status != LeaveStatus.Cancelled)
                RejectOverlaps(svc, target.Id, employee.Id, start.Value, end.Value);
        }

        private static void PostOperationRules(IPluginExecutionContext context, Entity target, Entity pre, IOrganizationService svc, ITracingService trace,
            Func<string, EntityReference> getRef, Func<string, DateTime?> getDate, Func<string, string> getText, Func<string, OptionSetValue> getOption)
        {
            // On Create the new id is only guaranteed in the output parameters
            var requestId = target.Id != Guid.Empty ? target.Id : (Guid)context.OutputParameters["id"];

            var approver = getRef("fwp_approver");
            if (approver != null && (context.MessageName == "Create" || target.Contains("fwp_approver")))
            {
                svc.Execute(new GrantAccessRequest
                {
                    Target = new EntityReference("fwp_leaverequest", requestId),
                    PrincipalAccess = new PrincipalAccess { Principal = approver, AccessMask = AccessRights.ReadAccess | AccessRights.WriteAccess }
                });
            }

            int status = getOption("fwp_status")?.Value ?? LeaveStatus.Pending;
            int? oldStatus = pre?.GetAttributeValue<OptionSetValue>("fwp_status")?.Value;
            bool datesChanged = target.Contains("fwp_startdate") || target.Contains("fwp_enddate") || target.Contains("fwp_starttime") || target.Contains("fwp_endtime");
            if (status == oldStatus && !datesChanged)
                return;

            var legacy = getText("fwp_legacyid");
            var sourceKey = !string.IsNullOrEmpty(legacy) ? legacy : "LV-" + requestId.ToString("N");
            var leave = new LeaveTimeEntryService(svc, trace);

            // Approved leave stays as time entries until it is fully cancelled; a pending cancellation keeps them
            if (status == LeaveStatus.Approved || status == LeaveStatus.CancellationRequested)
            {
                var end = getDate("fwp_enddate") ?? getDate("fwp_startdate").Value;
                var endTime = LeaveDayCalculator.ParseTime(getText("fwp_endtime"), true);
                leave.RecordLeave(getRef("fwp_employee").Id, getDate("fwp_startdate").Value, end,
                    LeaveDayCalculator.ParseTime(getText("fwp_starttime"), false), endTime,
                    LeaveTimeEntryService.KindLeave, sourceKey);
            }
            else
            {
                leave.RemoveLeave(sourceKey);
            }
        }

        private static EntityReference DefaultApprover(IOrganizationService svc, Guid employeeId)
        {
            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet("fwp_leaveapprover"), TopCount = 1 };
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var approver = svc.RetrieveMultiple(q).Entities.FirstOrDefault()?.GetAttributeValue<EntityReference>("fwp_leaveapprover");
            if (approver != null) return approver;

            // Fall back to the manager on the user record
            return svc.Retrieve("systemuser", employeeId, new ColumnSet("parentsystemuserid")).GetAttributeValue<EntityReference>("parentsystemuserid");
        }

        private static void RejectOverlaps(IOrganizationService svc, Guid requestId, Guid employeeId, DateTime start, DateTime end)
        {
            var q = new QueryExpression("fwp_leaverequest") { ColumnSet = new ColumnSet("fwp_startdate", "fwp_enddate"), TopCount = 1 };
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
