using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.Leave.Plugins
{
    /// <summary>
    /// Writes leave as fwp_timeentry rows (DHA-9). Every row carries fwp_leavesourcekey so a request can
    /// be re-applied (approval edited) or removed (cancelled) without touching anything else.
    /// </summary>
    public class LeaveTimeEntryService
    {
        // fwp_timeentry option values - must match DHA.DSTC.WPF Models/TimeEntry.cs
        private const int CategoryNonChargeable = 2;
        private const int ClassificationProject = 800470000;
        private const int ChargeRateA = 800470000;

        public const string KindLeave = "Leave";
        public const string KindBankHoliday = "BankHoliday";

        private readonly IOrganizationService _svc;
        private readonly ITracingService _trace;

        public LeaveTimeEntryService(IOrganizationService svc, ITracingService trace)
        {
            _svc = svc;
            _trace = trace;
        }

        public class Result
        {
            public int Created { get; set; }
            public int Removed { get; set; }
            public decimal TotalHours { get; set; }
        }

        /// <summary>Replaces all leave entries for <paramref name="sourceKey"/> with entries for the given range.</summary>
        public Result RecordLeave(Guid userId, DateTime startDate, DateTime endDate, TimeSpan? startTime, TimeSpan? endTime,
            string kind, string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
                throw new InvalidPluginExecutionException("SourceKey is required.");

            bool bankHoliday = string.Equals(kind, KindBankHoliday, StringComparison.OrdinalIgnoreCase);
            var settings = LeaveSettings.Load(_svc);
            var project = bankHoliday ? settings.BankHolidayProjectId : settings.LeaveProjectId;
            if (project == Guid.Empty)
                throw new InvalidPluginExecutionException(
                    $"Environment variable {(bankHoliday ? LeaveSettings.BankHolidayProjectVar : LeaveSettings.LeaveProjectVar)} is not set.");

            var pattern = LoadWorkingPattern(userId, settings.DefaultDayHours);
            // Leave never lands on a bank holiday - the bank holiday has its own entry
            var skip = bankHoliday ? null : LoadBankHolidays(startDate, endDate);
            var days = LeaveDayCalculator.Calculate(startDate, endDate, startTime, endTime, pattern, skip);

            var result = new Result();
            var keep = new HashSet<DateTime>();
            foreach (var existing in GetEntries(sourceKey))
            {
                // Reconciled entries are part of a billing run - never delete them, and don't duplicate their day
                if (existing.GetAttributeValue<object>("fwp_calculatedreconciledvalue") != null)
                {
                    keep.Add(existing.GetAttributeValue<DateTime>("fwp_date").Date);
                    continue;
                }
                _svc.Delete("fwp_timeentry", existing.Id);
                result.Removed++;
            }

            string label = bankHoliday ? settings.BankHolidayLabel : settings.LeaveLabel;
            foreach (var day in days.Where(d => !keep.Contains(d.Date)))
            {
                _svc.Create(BuildEntry(userId, project, day, label, sourceKey));
                result.Created++;
                result.TotalHours += day.Hours;
            }

            _trace?.Trace($"RecordLeave {sourceKey}: user {userId}, {startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd}, created {result.Created}, removed {result.Removed}, {result.TotalHours}h");
            return result;
        }

        /// <summary>Removes the (unreconciled) leave entries for a cancelled or rejected request.</summary>
        public int RemoveLeave(string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
                throw new InvalidPluginExecutionException("SourceKey is required.");

            int removed = 0;
            foreach (var existing in GetEntries(sourceKey))
            {
                if (existing.GetAttributeValue<object>("fwp_calculatedreconciledvalue") != null)
                {
                    _trace?.Trace($"RemoveLeave {sourceKey}: kept reconciled entry {existing.Id}");
                    continue;
                }
                _svc.Delete("fwp_timeentry", existing.Id);
                removed++;
            }
            _trace?.Trace($"RemoveLeave {sourceKey}: removed {removed}");
            return removed;
        }

        /// <summary>
        /// Gives every active colleague (with a colleague configuration) a bank holiday entry for each
        /// public holiday in the range, on their working days only. Idempotent per person per holiday.
        /// </summary>
        public Result RecordBankHolidays(DateTime fromDate, DateTime toDate)
        {
            var result = new Result();
            var holidays = LoadBankHolidays(fromDate, toDate);
            if (holidays.Count == 0)
                return result;

            foreach (var userId in GetActiveColleagues())
            {
                foreach (var date in holidays)
                {
                    var r = RecordLeave(userId, date, date, null, null, KindBankHoliday, BankHolidayKey(date, userId));
                    result.Created += r.Created;
                    result.Removed += r.Removed;
                    result.TotalHours += r.TotalHours;
                }
            }
            return result;
        }

        public static string BankHolidayKey(DateTime date, Guid userId) => $"BH-{date:yyyy-MM-dd}-{userId:N}";

        public Guid ResolveUser(Guid? userId, string email)
        {
            if (userId.HasValue && userId.Value != Guid.Empty)
                return userId.Value;
            if (string.IsNullOrWhiteSpace(email))
                throw new InvalidPluginExecutionException("UserId or UserEmail is required.");

            var q = new QueryExpression("systemuser") { ColumnSet = new ColumnSet(false), TopCount = 2 };
            var or = new FilterExpression(LogicalOperator.Or);
            or.AddCondition("internalemailaddress", ConditionOperator.Equal, email.Trim());
            or.AddCondition("domainname", ConditionOperator.Equal, email.Trim());
            q.Criteria.AddFilter(or);
            q.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);

            var users = _svc.RetrieveMultiple(q).Entities;
            if (users.Count != 1)
                throw new InvalidPluginExecutionException($"Could not find a single active Dynamics user for '{email}' (found {users.Count}).");
            return users[0].Id;
        }

        public WorkingDay[] LoadWorkingPattern(Guid userId, decimal defaultDayHours)
        {
            var names = new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };
            var cols = names.SelectMany(n => new[] { $"fwp_{n}expectedhours", $"fwp_{n}start", $"fwp_{n}finish" }).ToArray();

            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet(cols), TopCount = 1 };
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, userId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var cfg = _svc.RetrieveMultiple(q).Entities.FirstOrDefault();

            var pattern = LeaveDayCalculator.DefaultPattern(defaultDayHours);
            if (cfg == null)
            {
                _trace?.Trace($"No colleague configuration for {userId} - using Mon-Fri {defaultDayHours}h.");
                return pattern;
            }

            for (int i = 0; i < 7; i++)
            {
                var hours = cfg.GetAttributeValue<decimal?>($"fwp_{names[i]}expectedhours");
                if (!hours.HasValue)
                    continue; // not filled in - keep the default for that day
                pattern[i] = new WorkingDay
                {
                    ExpectedHours = hours.Value,
                    Start = LeaveDayCalculator.ParseTime(cfg.GetAttributeValue<string>($"fwp_{names[i]}start"), false) ?? new TimeSpan(9, 0, 0),
                    Finish = LeaveDayCalculator.ParseTime(cfg.GetAttributeValue<string>($"fwp_{names[i]}finish"), true) ?? new TimeSpan(17, 30, 0)
                };
            }
            return pattern;
        }

        public HashSet<DateTime> LoadBankHolidays(DateTime from, DateTime to)
        {
            var q = new QueryExpression("fwp_publicholiday") { ColumnSet = new ColumnSet("fwp_date") };
            q.Criteria.AddCondition("fwp_date", ConditionOperator.OnOrAfter, from.Date);
            q.Criteria.AddCondition("fwp_date", ConditionOperator.OnOrBefore, to.Date);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return new HashSet<DateTime>(_svc.RetrieveMultiple(q).Entities.Select(e => e.GetAttributeValue<DateTime>("fwp_date").Date));
        }

        private IEnumerable<Guid> GetActiveColleagues()
        {
            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet("fwp_colleague") };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.NotNull);
            var user = q.AddLink("systemuser", "fwp_colleague", "systemuserid");
            user.LinkCriteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            return _svc.RetrieveMultiple(q).Entities
                .Select(e => e.GetAttributeValue<EntityReference>("fwp_colleague").Id)
                .Distinct()
                .ToList();
        }

        private IEnumerable<Entity> GetEntries(string sourceKey)
        {
            var q = new QueryExpression("fwp_timeentry") { ColumnSet = new ColumnSet("fwp_date", "fwp_calculatedreconciledvalue") };
            q.Criteria.AddCondition("fwp_leavesourcekey", ConditionOperator.Equal, sourceKey);
            return _svc.RetrieveMultiple(q).Entities;
        }

        private static Entity BuildEntry(Guid userId, Guid projectId, LeaveDay day, string label, string sourceKey)
        {
            int wholeHours = (int)Math.Floor(day.Hours);
            int minutes = (int)Math.Round((day.Hours - wholeHours) * 60m);

            var e = new Entity("fwp_timeentry");
            e["fwp_date"] = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
            e["fwp_teammember"] = new EntityReference("systemuser", userId);
            e["fwp_project"] = new EntityReference("msdyn_project", projectId);
            e["fwp_classification"] = new OptionSetValue(ClassificationProject);
            e["fwp_category"] = new OptionSetValue(CategoryNonChargeable);
            e["fwp_chargerate"] = new OptionSetValue(ChargeRateA);
            e["fwp_durationhours"] = (decimal)wholeHours;
            e["fwp_minutes"] = minutes;
            e["fwp_decimalhours"] = day.Hours;
            e["fwp_notes"] = label;
            e["fwp_leavesourcekey"] = sourceKey;
            return e;
        }
    }
}
