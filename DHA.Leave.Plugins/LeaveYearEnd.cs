using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.Leave.Plugins
{
    /// <summary>
    /// Year-end rules (parity plan A3, D6; HR decisions 15 Oct 2026): unused days carry into the next leave year, capped at
    /// the standard limit (5) unless HR set an override on that person's row for the year being closed. Pending requests count
    /// as taken (Q6), so they aren't carried. Nothing below zero is carried - an overdrawn balance isn't taken off next year.
    /// </summary>
    public static class LeaveYearEnd
    {
        public const decimal StandardCarryForwardLimit = 5m;

        public static decimal CarryForwardDays(LeaveBalanceFigures closingYear, decimal standardLimit, decimal? overrideLimit)
        {
            var limit = overrideLimit ?? standardLimit;
            if (!closingYear.HasEntitlement || limit <= 0)
                return 0m;
            return Math.Round(Math.Min(Math.Max(closingYear.RemainingDays, 0m), limit), 2);
        }

        internal static string Days(decimal days) => days.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>The year to work on: the Year parameter, or this year (the flow runs on 1 January).</summary>
        internal static int YearInput(IPluginExecutionContext context)
        {
            int year = context.InputParameters.TryGetValue("Year", out var v) && v is int y && y > 1900 ? y : DateTime.UtcNow.Year;
            if (year > DateTime.UtcNow.Year + 1)
                throw new InvalidPluginExecutionException($"{year} is too far ahead - year-end runs for this year or next.");
            return year;
        }

        /// <summary>The EmployeeId parameter: one person instead of everyone (e.g. re-running after HR change an override).</summary>
        internal static Guid? EmployeeInput(IPluginExecutionContext context) =>
            context.InputParameters.TryGetValue("EmployeeId", out var v) && v is Guid g && g != Guid.Empty ? g : (Guid?)null;

        internal static Dictionary<Guid, Entity> EntitlementsFor(IOrganizationService svc, int year, Guid? employeeId, params string[] columns)
        {
            var q = new QueryExpression("fwp_leaveentitlement") { ColumnSet = new ColumnSet(columns.Concat(new[] { "fwp_employee" }).ToArray()) };
            q.Criteria.AddCondition("fwp_yearstart", ConditionOperator.On, new DateTime(year, 1, 1));
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            if (employeeId != null)
                q.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, employeeId.Value);
            var result = new Dictionary<Guid, Entity>();
            foreach (var e in svc.RetrieveMultiple(q).Entities)
            {
                var employee = e.GetAttributeValue<EntityReference>("fwp_employee");
                if (employee != null && !result.ContainsKey(employee.Id))
                    result[employee.Id] = e;
            }
            return result;
        }
    }

    /// <summary>
    /// Custom API fwp_GenerateLeaveYear - creates a leave year's entitlement rows from each person's base entitlement on
    /// colleague configuration (parity plan A4). Anyone who already has a row for the year is left alone, so it's safe to re-run.
    /// New starters still get their first row by hand (Q7): people with no base entitlement are listed, not given a row.
    /// In: Year? (default: this year), EmployeeId? (default: everyone). Out: Created (int), Report (text for HR).
    /// HR (leave administrators) only.
    /// </summary>
    public class GenerateLeaveYearPlugin : LeaveReadApiPluginBase
    {
        protected override void Run(IPluginExecutionContext context, IOrganizationService system, Guid callerId)
        {
            if (!LeaveAccess.IsLeaveAdmin(system, callerId))
                throw new InvalidPluginExecutionException("Only HR can run the leave year-end.");
            int year = LeaveYearEnd.YearInput(context);
            var employeeId = LeaveYearEnd.EmployeeInput(context);

            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet("fwp_colleague", "fwp_baseentitlement") };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.NotNull);
            if (employeeId != null)
                q.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, employeeId.Value);
            var user = q.AddLink("systemuser", "fwp_colleague", "systemuserid");
            user.EntityAlias = "u";
            user.Columns = new ColumnSet("fullname");
            user.LinkCriteria.AddCondition("isdisabled", ConditionOperator.Equal, false);

            var existing = LeaveYearEnd.EntitlementsFor(system, year, employeeId);
            var created = new List<string>();
            var noBase = new List<string>();
            int alreadyHad = 0;
            foreach (var cfg in system.RetrieveMultiple(q).Entities)
            {
                var colleague = cfg.GetAttributeValue<EntityReference>("fwp_colleague");
                var name = cfg.GetAttributeValue<AliasedValue>("u.fullname")?.Value as string ?? colleague.Id.ToString();
                if (existing.ContainsKey(colleague.Id))
                {
                    alreadyHad++;
                    continue;
                }
                var baseDays = cfg.GetAttributeValue<decimal?>("fwp_baseentitlement");
                if (baseDays == null)
                {
                    noBase.Add(name);
                    continue;
                }
                // LeaveEntitlementPlugin names the row and gives it to the employee
                system.Create(new Entity("fwp_leaveentitlement")
                {
                    ["fwp_employee"] = colleague,
                    ["fwp_yearstart"] = new DateTime(year, 1, 1),
                    ["fwp_entitlementdays"] = baseDays.Value
                });
                existing[colleague.Id] = null;
                created.Add($"{name}: {LeaveYearEnd.Days(baseDays.Value)} days");
            }

            var report = new StringBuilder();
            report.AppendLine($"{year} entitlements: {created.Count} created, {alreadyHad} already set up.");
            if (created.Count > 0)
                report.AppendLine().AppendLine("Created:").AppendLine(string.Join("\n", created.OrderBy(s => s)));
            if (noBase.Count > 0)
                report.AppendLine().AppendLine("No base entitlement in their leave settings, so nothing created - set it on the HR tab, then add their year:")
                      .AppendLine(string.Join("\n", noBase.OrderBy(s => s)));
            context.OutputParameters["Created"] = created.Count;
            context.OutputParameters["Report"] = report.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Custom API fwp_CarryForwardLeave - carries unused days from Year - 1 into Year (parity plan A3). For each person with
    /// rows for both years it SETS (never adds to) the old row's "carried to next year" and the new row's "carried forward",
    /// so it can be re-run after HR change an override or late changes to last year's leave.
    /// In: Year? (the new year; default: this year), EmployeeId? (default: everyone). Out: Carried (int, people updated),
    /// Report (text for HR).
    /// HR (leave administrators) only.
    /// </summary>
    public class CarryForwardLeavePlugin : LeaveReadApiPluginBase
    {
        protected override void Run(IPluginExecutionContext context, IOrganizationService system, Guid callerId)
        {
            if (!LeaveAccess.IsLeaveAdmin(system, callerId))
                throw new InvalidPluginExecutionException("Only HR can run the leave year-end.");
            int year = LeaveYearEnd.YearInput(context);
            int closing = year - 1;
            var employeeId = LeaveYearEnd.EmployeeInput(context);
            var settings = LeaveSettings.Load(system);

            var oldRows = LeaveYearEnd.EntitlementsFor(system, closing, employeeId, "fwp_entitlementdays", "fwp_carriedforwarddays",
                "fwp_adjustmentdays", "fwp_carryforwardoverride", "fwp_carriedtonextyeardays");
            var newRows = LeaveYearEnd.EntitlementsFor(system, year, employeeId, "fwp_carriedforwarddays");
            var requests = RequestsByPerson(system, closing, employeeId);
            var names = Names(system, oldRows.Keys);

            var lines = new List<string>();
            var missing = new List<string>();
            int carried = 0;
            // After the year has closed every approved day in it counts as taken; pending ones still count (Q6)
            var afterYearEnd = new DateTime(year, 1, 1);
            foreach (var pair in oldRows)
            {
                var old = pair.Value;
                var name = names.TryGetValue(pair.Key, out var n) ? n : pair.Key.ToString();
                var balance = LeaveBalanceCalculator.Calculate(closing,
                    old.GetAttributeValue<decimal?>("fwp_entitlementdays"), old.GetAttributeValue<decimal?>("fwp_carriedforwarddays"),
                    old.GetAttributeValue<decimal?>("fwp_adjustmentdays"), null, true,
                    requests.TryGetValue(pair.Key, out var theirs) ? theirs : new List<BalanceRequest>(), afterYearEnd);
                var overrideLimit = old.GetAttributeValue<decimal?>("fwp_carryforwardoverride");
                var days = LeaveYearEnd.CarryForwardDays(balance, settings.CarryForwardLimit, overrideLimit);

                if (!newRows.TryGetValue(pair.Key, out var next))
                {
                    missing.Add($"{name}: {LeaveYearEnd.Days(balance.RemainingDays)} unused");
                    continue;
                }

                if (old.GetAttributeValue<decimal?>("fwp_carriedtonextyeardays") != days)
                    system.Update(new Entity("fwp_leaveentitlement", old.Id) { ["fwp_carriedtonextyeardays"] = days });
                if (next.GetAttributeValue<decimal?>("fwp_carriedforwarddays") != days)
                    system.Update(new Entity("fwp_leaveentitlement", next.Id) { ["fwp_carriedforwarddays"] = days });
                carried++;

                var line = $"{name}: {LeaveYearEnd.Days(balance.RemainingDays)} unused, {LeaveYearEnd.Days(days)} carried";
                var lost = balance.RemainingDays - days;
                if (lost > 0)
                    line += $" ({LeaveYearEnd.Days(lost)} over the {(overrideLimit != null ? "override" : "limit")} of {LeaveYearEnd.Days(overrideLimit ?? settings.CarryForwardLimit)})";
                if (balance.PendingDays > 0)
                    line += $", including {LeaveYearEnd.Days(balance.PendingDays)} still pending";
                lines.Add(line);
            }

            var report = new StringBuilder();
            report.AppendLine($"Carry forward {closing} to {year}: {carried} people updated. Standard limit {LeaveYearEnd.Days(settings.CarryForwardLimit)} days.");
            if (lines.Count > 0)
                report.AppendLine().AppendLine(string.Join("\n", lines.OrderBy(s => s)));
            if (missing.Count > 0)
                report.AppendLine().AppendLine($"No {year} entitlement, so nothing carried - add their {year} year on the HR tab, then run this again:")
                      .AppendLine(string.Join("\n", missing.OrderBy(s => s)));
            context.OutputParameters["Carried"] = carried;
            context.OutputParameters["Report"] = report.ToString().TrimEnd();
        }

        private static Dictionary<Guid, List<BalanceRequest>> RequestsByPerson(IOrganizationService system, int year, Guid? employeeId)
        {
            var yearStart = new DateTime(year, 1, 1);
            var q = new QueryExpression("fwp_leaverequest")
            {
                ColumnSet = new ColumnSet("fwp_employee", "fwp_status", "fwp_startdate", "fwp_enddate", "fwp_days", "fwp_leavetype"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            q.Criteria.AddCondition("fwp_startdate", ConditionOperator.OnOrAfter, yearStart);
            q.Criteria.AddCondition("fwp_startdate", ConditionOperator.OnOrBefore, yearStart.AddYears(1).AddDays(-1));
            q.Criteria.AddCondition("fwp_status", ConditionOperator.In, OpenOrBooked.Cast<object>().ToArray());
            if (employeeId != null)
                q.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, employeeId.Value);
            var type = q.AddLink("fwp_leavetype", "fwp_leavetype", "fwp_leavetypeid", JoinOperator.LeftOuter);
            type.EntityAlias = "t";
            type.Columns = new ColumnSet("fwp_deductsfromentitlement");

            var result = new Dictionary<Guid, List<BalanceRequest>>();
            while (true)
            {
                var page = system.RetrieveMultiple(q);
                foreach (var e in page.Entities)
                {
                    var employee = e.GetAttributeValue<EntityReference>("fwp_employee");
                    if (employee == null)
                        continue;
                    if (!result.TryGetValue(employee.Id, out var list))
                        result[employee.Id] = list = new List<BalanceRequest>();
                    list.Add(new BalanceRequest
                    {
                        Status = e.GetAttributeValue<OptionSetValue>("fwp_status").Value,
                        StartDate = e.GetAttributeValue<DateTime>("fwp_startdate"),
                        EndDate = e.GetAttributeValue<DateTime?>("fwp_enddate") ?? e.GetAttributeValue<DateTime>("fwp_startdate"),
                        Days = e.GetAttributeValue<decimal?>("fwp_days") ?? 0m,
                        // no type = counts, as before types existed (same rule as fwp_GetLeaveBalance)
                        Deducts = e.GetAttributeValue<EntityReference>("fwp_leavetype") == null
                                  || (e.GetAttributeValue<AliasedValue>("t.fwp_deductsfromentitlement")?.Value as bool? ?? false)
                    });
                }
                if (!page.MoreRecords)
                    break;
                q.PageInfo.PageNumber++;
                q.PageInfo.PagingCookie = page.PagingCookie;
            }
            return result;
        }

        private static Dictionary<Guid, string> Names(IOrganizationService system, IEnumerable<Guid> userIds)
        {
            var ids = userIds.ToArray();
            var result = new Dictionary<Guid, string>();
            if (ids.Length == 0)
                return result;
            var q = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("fullname") };
            q.Criteria.AddCondition("systemuserid", ConditionOperator.In, ids.Cast<object>().ToArray());
            foreach (var u in system.RetrieveMultiple(q).Entities)
                result[u.Id] = u.GetAttributeValue<string>("fullname");
            return result;
        }
    }
}
