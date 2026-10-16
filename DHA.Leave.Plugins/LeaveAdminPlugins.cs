using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.Leave.Plugins
{
    /// <summary>
    /// Custom API fwp_SetLeaveSettings - HR set a person's leave settings on their colleague configuration (parity plan A1).
    /// In: EmployeeId; optional ApproverId, AdditionalApproverId (Guid.Empty clears), BaseEntitlement (days), and per day
    ///     MondayStart/MondayFinish/MondayBreak .. Friday* ("HH:mm"; all three empty = not a working day).
    ///     Anything omitted (or null) is left as it is. To change a day send its start and finish (a missing break = no break).
    ///
    /// Colleague configuration also holds charge rates, so HR's role only READS it. This API is the only way they write to it,
    /// and it writes nothing but the fields above. Expected hours per day are a formula column (finish - start - break) that
    /// reads the times by position, so "5:00" or "1:00" silently gives no hours: times are normalised to HH:mm here.
    /// These are the same working days timesheets use - Dynamics is the one place they're kept after cutover (F3).
    /// </summary>
    public class SetLeaveSettingsPlugin : LeaveReadApiPluginBase
    {
        private static readonly string[] Days = { "monday", "tuesday", "wednesday", "thursday", "friday" };

        protected override void Run(IPluginExecutionContext context, IOrganizationService system, Guid callerId)
        {
            if (!LeaveAccess.IsLeaveAdmin(system, callerId))
                throw new InvalidPluginExecutionException("Only HR can change leave settings.");

            var employeeId = Input<Guid?>(context, "EmployeeId") ?? Guid.Empty;
            if (employeeId == Guid.Empty)
                throw new InvalidPluginExecutionException("EmployeeId is required.");

            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var cfg = system.RetrieveMultiple(q).Entities.FirstOrDefault();
            if (cfg == null)
                throw new InvalidPluginExecutionException(
                    "This person has no colleague configuration in Dynamics yet. It's set up with their rates, so ask whoever does that first.");

            var update = new Entity("fwp_colleagueconfiguration", cfg.Id);
            SetApprover(context, update, "ApproverId", "fwp_leaveapprover", employeeId);
            SetApprover(context, update, "AdditionalApproverId", "fwp_additionalleaveapprover", employeeId);

            if (Given(context, "BaseEntitlement"))
            {
                var days = (decimal)context.InputParameters["BaseEntitlement"];
                if (days < 0 || days > 60)
                    throw new InvalidPluginExecutionException("Base entitlement must be between 0 and 60 days.");
                update["fwp_baseentitlement"] = days;
            }

            foreach (var day in Days)
            {
                var label = char.ToUpperInvariant(day[0]) + day.Substring(1);
                if (!new[] { "Start", "Finish", "Break" }.Any(k => Given(context, label + k)))
                    continue;
                if (!Given(context, label + "Start") || !Given(context, label + "Finish"))
                    throw new InvalidPluginExecutionException($"{label}: send the start and finish together (both empty for a day off).");
                var start = Time(context, label + "Start");
                var finish = Time(context, label + "Finish");
                var lunch = Time(context, label + "Break");
                if (start == null && finish == null)
                    lunch = null; // not a working day
                else if (start == null || finish == null || finish <= start)
                    throw new InvalidPluginExecutionException($"{label}: give a start and a finish time, finish after start - or neither for a day off.");
                else if (lunch != null && lunch.Value >= finish.Value - start.Value)
                    throw new InvalidPluginExecutionException($"{label}: the break is longer than the day.");
                update[$"fwp_{day}start"] = Format(start);
                update[$"fwp_{day}finish"] = Format(finish);
                update[$"fwp_{day}break"] = Format(lunch);
            }

            if (update.Attributes.Count > 0)
                system.Update(update);
        }

        /// <summary>
        /// The caller sent this parameter. Dataverse passes omitted optional parameters through as null, so Contains alone would
        /// read "not sent" as "clear it" - which wiped a day's times in testing. Empty string still means clear.
        /// </summary>
        private static bool Given(IPluginExecutionContext context, string parameter) =>
            context.InputParameters.TryGetValue(parameter, out var value) && value != null;

        /// <summary>"8:30", "08:30", "1:00" -> TimeSpan; empty -> null.</summary>
        private static TimeSpan? Time(IPluginExecutionContext context, string parameter)
        {
            var text = (Input<string>(context, parameter) ?? "").Trim();
            if (text.Length == 0)
                return null;
            if (TimeSpan.TryParseExact(text, new[] { @"h\:mm", @"hh\:mm" }, System.Globalization.CultureInfo.InvariantCulture, out var t)
                && t < TimeSpan.FromHours(24))
                return t;
            throw new InvalidPluginExecutionException($"{parameter}: '{text}' isn't a time - use HH:mm, e.g. 08:30.");
        }

        // Two-digit hours: the expected-hours formula reads the hours as characters 1-2 and the minutes as 4-5
        private static string Format(TimeSpan? t) => t?.ToString(@"hh\:mm");

        private static void SetApprover(IPluginExecutionContext context, Entity update, string parameter, string field, Guid employeeId)
        {
            if (!Given(context, parameter))
                return;
            var id = (Guid)context.InputParameters[parameter];
            if (id == employeeId)
                throw new InvalidPluginExecutionException("People can't approve their own leave.");
            update[field] = id == Guid.Empty ? null : new EntityReference("systemuser", id);
        }
    }

    /// <summary>
    /// Rules for fwp_leaveentitlement (parity plan A2, D7): one row per person per leave year, starting 1 January;
    /// named "&lt;person&gt; &lt;year&gt;"; owned by the employee so they see it under "own" (DHA-1) even when HR create it.
    ///
    /// REGISTER on fwp_leaveentitlement, synchronous:
    ///   Create, Pre-operation; Update, Pre-operation filtered on fwp_employee, fwp_yearstart with Pre Image "PreImage"
    ///   (fwp_employee, fwp_yearstart); Create, Post-operation.
    /// </summary>
    public class LeaveEntitlementPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            if (!context.InputParameters.TryGetValue("Target", out var boxed) || !(boxed is Entity target)
                || target.LogicalName != "fwp_leaveentitlement")
                return;
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var svc = ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory))).CreateOrganizationService(null);
            var pre = context.PreEntityImages.Contains("PreImage") ? context.PreEntityImages["PreImage"] : null;
            T Get<T>(string attr) => target.Contains(attr) ? target.GetAttributeValue<T>(attr) : pre != null ? pre.GetAttributeValue<T>(attr) : default(T);

            var employee = Get<EntityReference>("fwp_employee");
            var yearStart = Get<DateTime?>("fwp_yearstart");

            if (context.Stage == 20)
            {
                if (employee == null || yearStart == null)
                    throw new InvalidPluginExecutionException("An entitlement needs a person and a leave year.");
                if (yearStart.Value.Month != 1 || yearStart.Value.Day != 1)
                    throw new InvalidPluginExecutionException("Leave years start on 1 January.");

                var q = new QueryExpression("fwp_leaveentitlement") { ColumnSet = new ColumnSet(false), TopCount = 1 };
                q.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, employee.Id);
                q.Criteria.AddCondition("fwp_yearstart", ConditionOperator.On, yearStart.Value.Date);
                q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                if (context.MessageName == "Update")
                    q.Criteria.AddCondition("fwp_leaveentitlementid", ConditionOperator.NotEqual, target.Id);
                if (svc.RetrieveMultiple(q).Entities.Count > 0)
                    throw new InvalidPluginExecutionException($"This person already has an entitlement for {yearStart.Value.Year}. Change that one instead.");

                var name = svc.Retrieve("systemuser", employee.Id, new ColumnSet("fullname")).GetAttributeValue<string>("fullname");
                target["fwp_name"] = $"{name} {yearStart.Value.Year}";
            }
            else if (context.Stage == 40 && context.MessageName == "Create" && employee != null)
            {
                // Same as booked-on-behalf requests: only assign if the employee can read their own entitlement (DHA-1)
                if (LeaveAccess.HasPrivilege(svc, employee.Id, "prvReadfwp_LeaveEntitlement"))
                    svc.Update(new Entity("fwp_leaveentitlement", target.Id) { ["ownerid"] = employee });
                else
                    trace.Trace($"{employee.Id} can't read entitlements (no DHA-1?) - entitlement {target.Id} left with its creator.");
            }
        }
    }
}
