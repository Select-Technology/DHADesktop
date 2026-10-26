using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.Leave.Plugins
{
    /// <summary>
    /// Who may see whose leave (parity plan stage 3). Line managers only hold DHA-1, so they can't read other people's
    /// entitlements or the whole company's leave; these checks decide what the read-only Custom APIs below hand out.
    /// </summary>
    public static class LeaveAccess
    {
        /// <summary>Owner team whose members may book leave for other people (A9). Members are managed in Dynamics.</summary>
        public const string BookOnBehalfTeam = "Leave - Book on behalf";

        /// <summary>HR, or a member of the "Leave - Book on behalf" team.</summary>
        public static bool CanBookOnBehalf(IOrganizationService svc, Guid userId)
        {
            var q = new QueryExpression("team") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            q.Criteria.AddCondition("name", ConditionOperator.Equal, BookOnBehalfTeam);
            var member = q.AddLink("teammembership", "teamid", "teamid");
            member.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            return svc.RetrieveMultiple(q).Entities.Count > 0 || IsLeaveAdmin(svc, userId);
        }

        /// <summary>True if the user holds the privilege (any depth) through one of their roles or one of their teams' roles.</summary>
        public static bool HasPrivilege(IOrganizationService svc, Guid userId, string privilegeName)
        {
            string viaUser = $@"<fetch top='1'><entity name='privilege'><attribute name='privilegeid'/>
                <filter><condition attribute='name' operator='eq' value='{privilegeName}'/></filter>
                <link-entity name='roleprivileges' from='privilegeid' to='privilegeid' intersect='true'>
                  <link-entity name='role' from='roleid' to='roleid'>
                    <link-entity name='systemuserroles' from='roleid' to='roleid' intersect='true'>
                      <filter><condition attribute='systemuserid' operator='eq' value='{userId}'/></filter>
                    </link-entity></link-entity></link-entity></entity></fetch>";
            string viaTeam = $@"<fetch top='1'><entity name='privilege'><attribute name='privilegeid'/>
                <filter><condition attribute='name' operator='eq' value='{privilegeName}'/></filter>
                <link-entity name='roleprivileges' from='privilegeid' to='privilegeid' intersect='true'>
                  <link-entity name='role' from='roleid' to='roleid'>
                    <link-entity name='teamroles' from='roleid' to='roleid' intersect='true'>
                      <link-entity name='teammembership' from='teamid' to='teamid' intersect='true'>
                        <filter><condition attribute='systemuserid' operator='eq' value='{userId}'/></filter>
                      </link-entity></link-entity></link-entity></link-entity></entity></fetch>";
            return svc.RetrieveMultiple(new FetchExpression(viaUser)).Entities.Count > 0
                || svc.RetrieveMultiple(new FetchExpression(viaTeam)).Entities.Count > 0;
        }

        /// <summary>Organisation-wide read of leave requests: the "DHA - Leave Administrator" role (HR), or System Administrator.</summary>
        public static bool IsLeaveAdmin(IOrganizationService svc, Guid userId)
        {
            const int Global = 8;
            string RolesVia(string link) => $@"<fetch top='1'><entity name='privilege'><attribute name='privilegeid'/>
                <filter><condition attribute='name' operator='eq' value='prvReadfwp_LeaveRequest'/></filter>
                <link-entity name='roleprivileges' from='privilegeid' to='privilegeid' intersect='true'>
                  <filter><condition attribute='privilegedepthmask' operator='eq' value='{Global}'/></filter>
                  <link-entity name='role' from='roleid' to='roleid'>{link}</link-entity></link-entity></entity></fetch>";

            string viaUser = RolesVia($@"<link-entity name='systemuserroles' from='roleid' to='roleid' intersect='true'>
                <filter><condition attribute='systemuserid' operator='eq' value='{userId}'/></filter></link-entity>");
            string viaTeam = RolesVia($@"<link-entity name='teamroles' from='roleid' to='roleid' intersect='true'>
                <link-entity name='teammembership' from='teamid' to='teamid' intersect='true'>
                  <filter><condition attribute='systemuserid' operator='eq' value='{userId}'/></filter></link-entity></link-entity>");
            return svc.RetrieveMultiple(new FetchExpression(viaUser)).Entities.Count > 0
                || svc.RetrieveMultiple(new FetchExpression(viaTeam)).Entities.Count > 0;
        }

        /// <summary>The people whose leave this user approves (as approver or additional approver), from colleague configuration.</summary>
        public static HashSet<Guid> PeopleApprovedBy(IOrganizationService svc, Guid approverId)
        {
            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet("fwp_colleague") };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var either = new FilterExpression(LogicalOperator.Or);
            either.AddCondition("fwp_leaveapprover", ConditionOperator.Equal, approverId);
            either.AddCondition("fwp_additionalleaveapprover", ConditionOperator.Equal, approverId);
            q.Criteria.AddFilter(either);
            return new HashSet<Guid>(svc.RetrieveMultiple(q).Entities
                .Select(e => e.GetAttributeValue<EntityReference>("fwp_colleague")?.Id ?? Guid.Empty)
                .Where(id => id != Guid.Empty));
        }
    }

    /// <summary>
    /// Read-only leave Custom APIs. They check the caller themselves and then read as SYSTEM, because the caller
    /// (a line manager) can't read the rows directly.
    /// </summary>
    public abstract class LeaveReadApiPluginBase : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            try
            {
                Run(context, factory.CreateOrganizationService(null), context.InitiatingUserId);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                trace.Trace("{0} error: {1}", GetType().Name, ex);
                throw new InvalidPluginExecutionException($"{GetType().Name} failed: {ex.Message}", ex);
            }
        }

        protected abstract void Run(IPluginExecutionContext context, IOrganizationService system, Guid callerId);

        protected static T Input<T>(IPluginExecutionContext context, string name) =>
            context.InputParameters.TryGetValue(name, out var v) && v is T t ? t : default(T);

        protected static readonly int[] OpenOrBooked = { LeaveStatus.Pending, LeaveStatus.Approved, LeaveStatus.CancellationRequested };
    }

    /// <summary>
    /// Custom API fwp_GetLeaveBalance - one person's balance for one leave year, in days.
    /// In: EmployeeId? (default: the caller), Year? (default: this year).
    /// Out: HasEntitlement, BaseDays, CarriedForwardDays, AdjustmentDays, CarriedToNextYearDays, TakenDays, BookedDays, PendingDays.
    /// Allowed for the employee, their approver or additional approver, leave administrators, and the book-on-behalf team
    /// (who see the balance of the person they're booking for).
    /// </summary>
    public class GetLeaveBalancePlugin : LeaveReadApiPluginBase
    {
        protected override void Run(IPluginExecutionContext context, IOrganizationService system, Guid callerId)
        {
            var employeeId = Input<Guid?>(context, "EmployeeId") is Guid g && g != Guid.Empty ? g : callerId;
            int year = Input<int?>(context, "Year") is int y && y > 1900 ? y : DateTime.UtcNow.Year;

            if (employeeId != callerId && !LeaveAccess.PeopleApprovedBy(system, callerId).Contains(employeeId)
                && !LeaveAccess.CanBookOnBehalf(system, callerId))
                throw new InvalidPluginExecutionException("You can only see the leave balance of people whose leave you approve.");

            var yearStart = new DateTime(year, 1, 1);
            var eq = new QueryExpression("fwp_leaveentitlement")
            {
                ColumnSet = new ColumnSet("fwp_entitlementdays", "fwp_carriedforwarddays", "fwp_adjustmentdays", "fwp_carriedtonextyeardays"),
                TopCount = 1
            };
            eq.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, employeeId);
            eq.Criteria.AddCondition("fwp_yearstart", ConditionOperator.On, yearStart);
            eq.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var ent = system.RetrieveMultiple(eq).Entities.FirstOrDefault();

            // fwp_leavetype must be read: without it every request looks untyped and counts, Working From Home included
            var rq = new QueryExpression("fwp_leaverequest") { ColumnSet = new ColumnSet("fwp_status", "fwp_startdate", "fwp_enddate", "fwp_days", "fwp_leavetype") };
            rq.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, employeeId);
            rq.Criteria.AddCondition("fwp_startdate", ConditionOperator.OnOrAfter, yearStart);
            rq.Criteria.AddCondition("fwp_startdate", ConditionOperator.OnOrBefore, yearStart.AddYears(1).AddDays(-1));
            rq.Criteria.AddCondition("fwp_status", ConditionOperator.In, OpenOrBooked.Cast<object>().ToArray());
            var type = rq.AddLink("fwp_leavetype", "fwp_leavetype", "fwp_leavetypeid", JoinOperator.LeftOuter);
            type.EntityAlias = "t";
            type.Columns = new ColumnSet("fwp_deductsfromentitlement");

            var requests = system.RetrieveMultiple(rq).Entities.Select(e => new BalanceRequest
            {
                Status = e.GetAttributeValue<OptionSetValue>("fwp_status").Value,
                StartDate = e.GetAttributeValue<DateTime>("fwp_startdate"),
                EndDate = e.GetAttributeValue<DateTime?>("fwp_enddate") ?? e.GetAttributeValue<DateTime>("fwp_startdate"),
                Days = e.GetAttributeValue<decimal?>("fwp_days") ?? 0m,
                // no type = counts, as before types existed
                Deducts = e.GetAttributeValue<EntityReference>("fwp_leavetype") == null
                          || (e.GetAttributeValue<AliasedValue>("t.fwp_deductsfromentitlement")?.Value as bool? ?? false)
            });

            var b = LeaveBalanceCalculator.Calculate(year,
                ent?.GetAttributeValue<decimal?>("fwp_entitlementdays"), ent?.GetAttributeValue<decimal?>("fwp_carriedforwarddays"),
                ent?.GetAttributeValue<decimal?>("fwp_adjustmentdays"), ent?.GetAttributeValue<decimal?>("fwp_carriedtonextyeardays"),
                ent != null, requests, DateTime.UtcNow.Date);

            context.OutputParameters["HasEntitlement"] = b.HasEntitlement;
            context.OutputParameters["BaseDays"] = b.BaseDays;
            context.OutputParameters["CarriedForwardDays"] = b.CarriedForwardDays;
            context.OutputParameters["AdjustmentDays"] = b.AdjustmentDays;
            context.OutputParameters["CarriedToNextYearDays"] = b.CarriedToNextYearDays;
            context.OutputParameters["TakenDays"] = b.TakenDays;
            context.OutputParameters["BookedDays"] = b.BookedDays;
            context.OutputParameters["PendingDays"] = b.PendingDays;
        }
    }

    /// <summary>
    /// Custom API fwp_GetTeamLeave - everyone's pending and booked leave overlapping a date range ("who's off", M3).
    /// In: FromDate, ToDate (at most a year apart). Out: Leave (fwp_leaverequest rows: employee, dates, times, status, days).
    /// The type is only included for the caller's own people (or for leave administrators) and for non-absence types such as
    /// Working From Home - otherwise it's withheld, like the shared calendar's "Out of Office".
    /// Allowed for leave administrators and anyone who approves someone's leave.
    /// </summary>
    public class GetTeamLeavePlugin : LeaveReadApiPluginBase
    {
        protected override void Run(IPluginExecutionContext context, IOrganizationService system, Guid callerId)
        {
            var from = Input<DateTime?>(context, "FromDate") ?? throw new InvalidPluginExecutionException("FromDate is required.");
            var to = Input<DateTime?>(context, "ToDate") ?? throw new InvalidPluginExecutionException("ToDate is required.");
            if (to < from || to > from.AddYears(1))
                throw new InvalidPluginExecutionException("ToDate must be on or after FromDate and at most a year later.");

            bool admin = LeaveAccess.IsLeaveAdmin(system, callerId);
            var mine = LeaveAccess.PeopleApprovedBy(system, callerId);
            if (!admin && mine.Count == 0)
                throw new InvalidPluginExecutionException("Only leave approvers can see team leave.");

            var q = new QueryExpression("fwp_leaverequest")
            {
                ColumnSet = new ColumnSet("fwp_employee", "fwp_startdate", "fwp_enddate", "fwp_starttime", "fwp_endtime", "fwp_status", "fwp_days", "fwp_leavetype"),
                Orders = { new OrderExpression("fwp_startdate", OrderType.Ascending) }
            };
            q.Criteria.AddCondition("fwp_status", ConditionOperator.In, OpenOrBooked.Cast<object>().ToArray());
            q.Criteria.AddCondition("fwp_startdate", ConditionOperator.OnOrBefore, to.Date);
            // one-day requests saved before the plug-in filled in the end date have none
            var ends = new FilterExpression(LogicalOperator.Or);
            ends.AddCondition("fwp_enddate", ConditionOperator.OnOrAfter, from.Date);
            var oneDay = ends.AddFilter(LogicalOperator.And);
            oneDay.AddCondition("fwp_enddate", ConditionOperator.Null);
            oneDay.AddCondition("fwp_startdate", ConditionOperator.OnOrAfter, from.Date);
            q.Criteria.AddFilter(ends);
            var type = q.AddLink("fwp_leavetype", "fwp_leavetype", "fwp_leavetypeid", JoinOperator.LeftOuter);
            type.EntityAlias = "t";
            type.Columns = new ColumnSet("fwp_isabsence");

            var result = new EntityCollection { EntityName = "fwp_leaverequest" };
            foreach (var e in system.RetrieveMultiple(q).Entities)
            {
                var employee = e.GetAttributeValue<EntityReference>("fwp_employee");
                bool absence = e.GetAttributeValue<AliasedValue>("t.fwp_isabsence")?.Value as bool? ?? true;
                var row = new Entity("fwp_leaverequest", e.Id) { ["fwp_leaverequestid"] = e.Id };
                foreach (var attr in new[] { "fwp_employee", "fwp_startdate", "fwp_enddate", "fwp_starttime", "fwp_endtime", "fwp_status", "fwp_days" })
                    if (e.Contains(attr)) row[attr] = e[attr];
                if (e.Contains("fwp_leavetype") && (admin || !absence || (employee != null && mine.Contains(employee.Id))))
                    row["fwp_leavetype"] = e["fwp_leavetype"];
                result.Entities.Add(row);
            }
            context.OutputParameters["Leave"] = result;
        }
    }

    /// <summary>
    /// When HR changes someone's leave approver or additional approver in colleague configuration, the new approvers get
    /// access to that person's leave from the start of last year (so they can see history and balances), and requests still
    /// waiting for a decision move to them.
    ///
    /// REGISTER on fwp_colleagueconfiguration, synchronous Post-operation: Create, and Update filtered on
    /// fwp_leaveapprover, fwp_additionalleaveapprover.
    /// </summary>
    public class LeaveApproverChangePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            if (!context.InputParameters.TryGetValue("Target", out var boxed) || !(boxed is Entity target)
                || target.LogicalName != "fwp_colleagueconfiguration")
                return;
            if (!target.Contains("fwp_leaveapprover") && !target.Contains("fwp_additionalleaveapprover"))
                return;

            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var svc = ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory))).CreateOrganizationService(null);
            try
            {
                var cfg = svc.Retrieve("fwp_colleagueconfiguration", target.Id,
                    new ColumnSet("fwp_colleague", "fwp_leaveapprover", "fwp_additionalleaveapprover"));
                var colleague = cfg.GetAttributeValue<EntityReference>("fwp_colleague");
                var approver = cfg.GetAttributeValue<EntityReference>("fwp_leaveapprover");
                var additional = cfg.GetAttributeValue<EntityReference>("fwp_additionalleaveapprover");
                if (colleague == null)
                    return;

                var q = new QueryExpression("fwp_leaverequest") { ColumnSet = new ColumnSet("fwp_status", "fwp_approver", "fwp_additionalapprover") };
                q.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, colleague.Id);
                // by start date: requests can't span two years, and older rows may have no end date
                q.Criteria.AddCondition("fwp_startdate", ConditionOperator.OnOrAfter, new DateTime(DateTime.UtcNow.Year - 1, 1, 1));
                foreach (var request in svc.RetrieveMultiple(q).Entities)
                {
                    foreach (var principal in new[] { approver, additional }.Where(p => p != null))
                        svc.Execute(new GrantAccessRequest
                        {
                            Target = request.ToEntityReference(),
                            PrincipalAccess = new PrincipalAccess { Principal = principal, AccessMask = AccessRights.ReadAccess | AccessRights.WriteAccess }
                        });

                    int status = request.GetAttributeValue<OptionSetValue>("fwp_status")?.Value ?? LeaveStatus.Pending;
                    bool waiting = status == LeaveStatus.Pending || status == LeaveStatus.CancellationRequested;
                    if (waiting && approver != null
                        && (request.GetAttributeValue<EntityReference>("fwp_approver")?.Id != approver.Id
                            || request.GetAttributeValue<EntityReference>("fwp_additionalapprover")?.Id != additional?.Id))
                    {
                        svc.Update(new Entity("fwp_leaverequest", request.Id) { ["fwp_approver"] = approver, ["fwp_additionalapprover"] = additional });
                    }
                }
            }
            catch (Exception ex)
            {
                trace.Trace("LeaveApproverChangePlugin error: {0}", ex);
                throw new InvalidPluginExecutionException("Couldn't pass this person's leave to their new approver: " + ex.Message, ex);
            }
        }
    }
}
