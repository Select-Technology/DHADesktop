using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.Leave.Plugins
{
    /// <summary>
    /// Writes leave as fwp_timeentry rows (DHA-9). Every row carries fwp_leavesourcekey so a request can
    /// be re-applied (approval edited) or withdrawn (cancelled) without touching anything else.
    ///
    /// Time entries are never deleted (DHA-10): entries no longer wanted are deactivated, and a day that comes
    /// back (e.g. a cancelled request re-approved) reactivates its old entry rather than creating a duplicate.
    /// Reconciled entries are part of a billing run and are never changed.
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

        private const int StateActive = 0, StateInactive = 1;
        private const int StatusActive = 1, StatusInactive = 2;

        public class Result
        {
            /// <summary>Entries created or reactivated.</summary>
            public int Created { get; set; }
            /// <summary>Entries deactivated (never deleted).</summary>
            public int Removed { get; set; }
            public int Updated { get; set; }
            public decimal TotalHours { get; set; }
        }

        /// <summary>Makes the active leave entries for <paramref name="sourceKey"/> match the given range, one per working day.</summary>
        public Result RecordLeave(Guid userId, DateTime startDate, DateTime endDate, TimeSpan? startTime, TimeSpan? endTime,
            string kind, string sourceKey, bool startHalfDay = false, bool endHalfDay = false)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
                throw new InvalidPluginExecutionException("SourceKey is required.");

            bool bankHoliday = string.Equals(kind, KindBankHoliday, StringComparison.OrdinalIgnoreCase);
            var settings = LeaveSettings.Load(_svc);
            var project = bankHoliday ? settings.BankHolidayProjectId : settings.LeaveProjectId;
            if (project == Guid.Empty)
                throw new InvalidPluginExecutionException(
                    $"Environment variable {(bankHoliday ? LeaveSettings.BankHolidayProjectVar : LeaveSettings.LeaveProjectVar)} is not set.");

            var pattern = LoadWorkingPattern(userId, settings.LeaveDayHours);
            // Leave never lands on a bank holiday - the bank holiday has its own entry
            var skip = bankHoliday ? null : LoadBankHolidays(startDate, endDate);
            var days = LeaveDayCalculator.Calculate(startDate, endDate, startTime, endTime, pattern, skip, startHalfDay, endHalfDay);

            var result = new Result();
            var existing = GetEntries(sourceKey).ToList();
            var reconciledDays = new HashSet<DateTime>(existing.Where(IsReconciled).Select(DateOf));
            var editable = existing.Where(e => !IsReconciled(e)).ToList();
            var claimed = new HashSet<Guid>();

            string label = bankHoliday ? settings.BankHolidayLabel : settings.LeaveLabel;
            foreach (var day in days.Where(d => !reconciledDays.Contains(d.Date)))
            {
                // Prefer the active entry for that day; otherwise bring back an inactive one; otherwise create
                var match = editable.Where(e => DateOf(e) == day.Date && !claimed.Contains(e.Id))
                                    .OrderBy(e => IsActive(e) ? 0 : 1).FirstOrDefault();
                if (match == null)
                {
                    _svc.Create(BuildEntry(userId, project, day, label, sourceKey));
                    result.Created++;
                }
                else
                {
                    claimed.Add(match.Id);
                    bool reactivate = !IsActive(match);
                    if (reactivate || match.GetAttributeValue<decimal?>("fwp_decimalhours") != day.Hours)
                    {
                        var update = new Entity("fwp_timeentry", match.Id);
                        SetHours(update, day.Hours);
                        if (reactivate)
                        {
                            update["statecode"] = new OptionSetValue(StateActive);
                            update["statuscode"] = new OptionSetValue(StatusActive);
                            result.Created++;
                        }
                        else
                        {
                            result.Updated++;
                        }
                        _svc.Update(update);
                    }
                }
                result.TotalHours += day.Hours;
            }

            // Anything still active that the request no longer covers is deactivated, never deleted
            foreach (var leftover in editable.Where(e => IsActive(e) && !claimed.Contains(e.Id)))
            {
                Deactivate(leftover.Id);
                result.Removed++;
            }

            _trace?.Trace($"RecordLeave {sourceKey}: user {userId}, {startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd}, " +
                          $"created/reactivated {result.Created}, updated {result.Updated}, deactivated {result.Removed}, {result.TotalHours}h");
            return result;
        }

        /// <summary>
        /// Cancelled or rejected request: deactivates its active, unreconciled leave entries (DHA-10 - never deleted).
        /// Returns how many were deactivated.
        /// </summary>
        public int RemoveLeave(string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
                throw new InvalidPluginExecutionException("SourceKey is required.");

            int removed = 0;
            foreach (var existing in GetEntries(sourceKey).Where(IsActive))
            {
                if (IsReconciled(existing))
                {
                    _trace?.Trace($"RemoveLeave {sourceKey}: kept reconciled entry {existing.Id}");
                    continue;
                }
                Deactivate(existing.Id);
                removed++;
            }
            _trace?.Trace($"RemoveLeave {sourceKey}: deactivated {removed}");
            return removed;
        }

        private void Deactivate(Guid timeEntryId)
        {
            _svc.Update(new Entity("fwp_timeentry", timeEntryId)
            {
                ["statecode"] = new OptionSetValue(StateInactive),
                ["statuscode"] = new OptionSetValue(StatusInactive)
            });
        }

        private static bool IsReconciled(Entity e) => e.GetAttributeValue<object>("fwp_calculatedreconciledvalue") != null;
        private static bool IsActive(Entity e) => (e.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? StateActive) == StateActive;
        private static DateTime DateOf(Entity e) => e.GetAttributeValue<DateTime>("fwp_date").Date;

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

            // Runs daily, so only fill in who's missing rather than recreating everyone's entries
            var existing = GetExistingKeys("BH-", fromDate, toDate);
            foreach (var userId in GetActiveColleagues())
            {
                foreach (var date in holidays.Where(d => !existing.Contains(BankHolidayKey(d, userId))))
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

        /// <summary>Working days from colleague configuration; hours per leave day from the fwp_LeaveDayHours setting.</summary>
        public WorkingDay[] LoadWorkingPattern(Guid userId, decimal[] leaveDayHours)
        {
            // Colleague configuration only has Monday-Friday; weekends stay as non-working days
            var names = new[] { "monday", "tuesday", "wednesday", "thursday", "friday" };
            var cols = names.SelectMany(n => new[] { $"fwp_{n}expectedhours", $"fwp_{n}start", $"fwp_{n}finish" }).ToArray();

            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet(cols), TopCount = 1 };
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, userId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var cfg = _svc.RetrieveMultiple(q).Entities.FirstOrDefault();

            if (cfg == null)
            {
                _trace?.Trace($"No colleague configuration for {userId} - using the standard working week.");
                return LeaveDayCalculator.BuildPattern(leaveDayHours, null);
            }
            return LeaveDayCalculator.BuildPattern(leaveDayHours,
                names.Select(n => cfg.GetAttributeValue<decimal?>($"fwp_{n}expectedhours")).ToArray(),
                names.Select(n => cfg.GetAttributeValue<string>($"fwp_{n}start")).ToArray(),
                names.Select(n => cfg.GetAttributeValue<string>($"fwp_{n}finish")).ToArray());
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

        private HashSet<string> GetExistingKeys(string prefix, DateTime from, DateTime to)
        {
            var q = new QueryExpression("fwp_timeentry") { ColumnSet = new ColumnSet("fwp_leavesourcekey") };
            q.Criteria.AddCondition("fwp_leavesourcekey", ConditionOperator.BeginsWith, prefix);
            q.Criteria.AddCondition("fwp_date", ConditionOperator.OnOrAfter, from.Date);
            q.Criteria.AddCondition("fwp_date", ConditionOperator.OnOrBefore, to.Date);
            return new HashSet<string>(_svc.RetrieveMultiple(q).Entities.Select(e => e.GetAttributeValue<string>("fwp_leavesourcekey")));
        }

        private IEnumerable<Entity> GetEntries(string sourceKey)
        {
            var q = new QueryExpression("fwp_timeentry")
            {
                ColumnSet = new ColumnSet("fwp_date", "fwp_decimalhours", "fwp_calculatedreconciledvalue", "statecode")
            };
            q.Criteria.AddCondition("fwp_leavesourcekey", ConditionOperator.Equal, sourceKey);
            return _svc.RetrieveMultiple(q).Entities;
        }

        // Same three fields the desktop app writes (Models/TimeEntry.ToEntity)
        private static void SetHours(Entity e, decimal hours)
        {
            int wholeHours = (int)Math.Floor(hours);
            e["fwp_durationhours"] = (decimal)wholeHours;
            e["fwp_minutes"] = (int)Math.Round((hours - wholeHours) * 60m);
            e["fwp_decimalhours"] = hours;
        }

        private static Entity BuildEntry(Guid userId, Guid projectId, LeaveDay day, string label, string sourceKey)
        {
            var e = new Entity("fwp_timeentry");
            e["fwp_date"] = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
            e["fwp_teammember"] = new EntityReference("systemuser", userId);
            e["fwp_project"] = new EntityReference("msdyn_project", projectId);
            e["fwp_classification"] = new OptionSetValue(ClassificationProject);
            e["fwp_category"] = new OptionSetValue(CategoryNonChargeable);
            e["fwp_chargerate"] = new OptionSetValue(ChargeRateA);
            SetHours(e, day.Hours);
            e["fwp_notes"] = label;
            e["fwp_leavesourcekey"] = sourceKey;
            return e;
        }
    }
}
