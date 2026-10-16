using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using DHA.Leave.Plugins;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DHA.DSTC.WPF.Services
{
    /// <summary>
    /// Leave booking (DHA-9 phase B). Hours shown in the preview use the same LeaveDayCalculator the
    /// server plug-in uses, so what the user sees is what gets stored and written to their timesheet.
    /// </summary>
    public class LeaveService
    {
        private readonly DataverseConnector _connector;
        // Colleague configuration only has Monday-Friday; weekends stay as non-working days
        private static readonly string[] Days = { "monday", "tuesday", "wednesday", "thursday", "friday" };

        // Who sees the Approvals screen: members of the HR Microsoft 365 group (HR@dhaplanning.co.uk, synced to a
        // Dataverse group team by provision_leave_approvers.py) and, for now, stsadmin
        private const string ApproverGroupObjectId = "5760dd7d-5244-4e06-b8fa-6ce9284e9d3c";
        private static readonly string[] ApproverEmails = { "stsadmin@dhaplanning.co.uk" };
        // Members may book leave for other people (A9) - same name as LeaveAccess.BookOnBehalfTeam in the plug-in
        private const string BookOnBehalfTeam = "Leave - Book on behalf";

        private const int ForcedUnitsHours = 800470001;

        private static readonly string[] RequestColumns =
        {
            "fwp_employee", "fwp_leavetype", "fwp_startdate", "fwp_enddate", "fwp_starttime", "fwp_endtime",
            "fwp_hours", "fwp_days", "fwp_status", "fwp_approver", "fwp_additionalapprover", "fwp_requestercomments", "fwp_approvercomments",
            "fwp_cancellationcomments", "fwp_cancellationrequestedon", "createdon"
        };

        public LeaveService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public List<LeaveRequest> GetMyRequests(Guid employeeId, DateTime from)
        {
            if (!_connector.Connect())
                return new List<LeaveRequest>();

            var q = new QueryExpression("fwp_leaverequest")
            {
                ColumnSet = new ColumnSet(RequestColumns),
                Orders = { new OrderExpression("fwp_startdate", OrderType.Descending) }
            };
            q.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("fwp_enddate", ConditionOperator.OnOrAfter, from.Date);

            return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q))
                .Entities.Select(LeaveRequest.FromEntity).ToList();
        }

        public List<LeaveType> GetLeaveTypes()
        {
            if (!_connector.Connect())
                return new List<LeaveType>();

            var q = new QueryExpression("fwp_leavetype")
            {
                ColumnSet = new ColumnSet("fwp_name", "fwp_deductsfromentitlement", "fwp_isabsence", "fwp_forcedunits", "fwp_sortorder"),
                Orders = { new OrderExpression("fwp_sortorder", OrderType.Ascending), new OrderExpression("fwp_name", OrderType.Ascending) }
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities
                .Select(e => new LeaveType
                {
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("fwp_name"),
                    DeductsFromEntitlement = e.GetAttributeValue<bool?>("fwp_deductsfromentitlement") ?? true,
                    IsAbsence = e.GetAttributeValue<bool?>("fwp_isabsence") ?? true,
                    BookedInHours = e.GetAttributeValue<OptionSetValue>("fwp_forcedunits")?.Value == ForcedUnitsHours
                }).ToList();
        }

        /// <summary>Submits a new request. The server works out the hours, approver and status, and rejects overlaps.</summary>
        public Guid SubmitRequest(LeaveRequest request)
        {
            if (!_connector.Connect())
                throw new InvalidOperationException("Not connected to Dynamics.");
            return _connector._orgService.Create(request.ToEntity());
        }

        /// <summary>
        /// A pending request is cancelled outright; approved leave goes to the approver to confirm. The comment is kept on the request.
        /// </summary>
        public LeaveRequestStatus CancelRequest(LeaveRequest request, string comment)
        {
            if (!_connector.Connect())
                throw new InvalidOperationException("Not connected to Dynamics.");

            var status = request.CancelsInstantly ? LeaveRequestStatus.Cancelled : LeaveRequestStatus.CancellationRequested;
            var e = new Entity("fwp_leaverequest", request.Id) { ["fwp_status"] = new OptionSetValue((int)status) };
            if (!string.IsNullOrWhiteSpace(comment))
                e["fwp_cancellationcomments"] = comment.Trim();
            _connector._orgService.Update(e);
            return status;
        }

        /// <summary>The person's leave approver and additional approver, from colleague configuration.</summary>
        public (string approver, string additional) GetApprovers(Guid employeeId)
        {
            if (!_connector.Connect())
                return (null, null);
            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet("fwp_leaveapprover", "fwp_additionalleaveapprover"), TopCount = 1 };
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var cfg = _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities.FirstOrDefault();
            return (cfg?.GetAttributeValue<EntityReference>("fwp_leaveapprover")?.Name,
                    cfg?.GetAttributeValue<EntityReference>("fwp_additionalleaveapprover")?.Name);
        }

        /// <summary>Company (bank) holidays with their names, for the holidays list.</summary>
        public List<(DateTime date, string name)> GetCompanyHolidays(DateTime from, DateTime to)
        {
            if (!_connector.Connect())
                return new List<(DateTime, string)>();
            var q = new QueryExpression("fwp_publicholiday")
            {
                ColumnSet = new ColumnSet("fwp_date", "fwp_name"),
                Orders = { new OrderExpression("fwp_date", OrderType.Ascending) }
            };
            q.Criteria.AddCondition("fwp_date", ConditionOperator.OnOrAfter, from.Date);
            q.Criteria.AddCondition("fwp_date", ConditionOperator.OnOrBefore, to.Date);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities
                .Select(e => (e.GetAttributeValue<DateTime>("fwp_date").Date, e.GetAttributeValue<string>("fwp_name"))).ToList();
        }

        // ---------- Approvals ----------

        public bool CanApprove(Guid userId, string email)
        {
            if (!string.IsNullOrEmpty(email) && ApproverEmails.Any(a => a.Equals(email, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (userId == Guid.Empty || !_connector.Connect())
                return false;

            try
            {
                var q = new QueryExpression("team") { ColumnSet = new ColumnSet(false), TopCount = 1 };
                q.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, new Guid(ApproverGroupObjectId));
                var member = q.AddLink("teammembership", "teamid", "teamid");
                member.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
                return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LeaveService.CanApprove: {ex.Message}");
                return false;
            }
        }

        /// <summary>HR (leave administrator) and the people this user approves, from colleague configuration.</summary>
        public ApproverScope GetApproverScope(Guid userId, string email)
        {
            var scope = new ApproverScope { IsLeaveAdmin = CanApprove(userId, email) };
            if (userId == Guid.Empty || !_connector.Connect())
                return scope;

            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet("fwp_colleague") };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var either = new FilterExpression(LogicalOperator.Or);
            either.AddCondition("fwp_leaveapprover", ConditionOperator.Equal, userId);
            either.AddCondition("fwp_additionalleaveapprover", ConditionOperator.Equal, userId);
            q.Criteria.AddFilter(either);
            scope.MyPeople = People(_connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)));
            return scope;
        }

        /// <summary>HR, or a member of the "Leave - Book on behalf" team (A9). The server checks this again when the request is saved.</summary>
        public bool CanBookOnBehalf(Guid userId, ApproverScope scope)
        {
            if (scope?.IsLeaveAdmin == true)
                return true;
            if (userId == Guid.Empty || !_connector.Connect())
                return false;
            try
            {
                var q = new QueryExpression("team") { ColumnSet = new ColumnSet(false), TopCount = 1 };
                q.Criteria.AddCondition("name", ConditionOperator.Equal, BookOnBehalfTeam);
                var member = q.AddLink("teammembership", "teamid", "teamid");
                member.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
                return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LeaveService.CanBookOnBehalf: {ex.Message}");
                return false;
            }
        }

        // ---------- HR administration (A1, A2) ----------

        private static readonly string[] WeekDays = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };

        /// <summary>Leave settings from colleague configuration (HR can read it; they write through fwp_SetLeaveSettings).</summary>
        public LeaveSettings GetLeaveSettings(Guid employeeId)
        {
            var settings = new LeaveSettings { EmployeeId = employeeId };
            if (!_connector.Connect())
                return settings;
            var cols = new List<string> { "fwp_leaveapprover", "fwp_additionalleaveapprover", "fwp_baseentitlement" };
            cols.AddRange(Days.SelectMany(d => new[] { $"fwp_{d}start", $"fwp_{d}finish", $"fwp_{d}break", $"fwp_{d}expectedhours" }));
            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet(cols.ToArray()), TopCount = 1 };
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var cfg = _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities.FirstOrDefault();
            if (cfg == null)
                return settings;

            settings.HasConfiguration = true;
            settings.ApproverId = cfg.GetAttributeValue<EntityReference>("fwp_leaveapprover")?.Id;
            settings.AdditionalApproverId = cfg.GetAttributeValue<EntityReference>("fwp_additionalleaveapprover")?.Id;
            settings.BaseEntitlement = cfg.GetAttributeValue<decimal?>("fwp_baseentitlement");
            for (int i = 0; i < Days.Length; i++)
                settings.Week.Add(new WorkingTimes
                {
                    Day = WeekDays[i],
                    Start = cfg.GetAttributeValue<string>($"fwp_{Days[i]}start"),
                    Finish = cfg.GetAttributeValue<string>($"fwp_{Days[i]}finish"),
                    Break = cfg.GetAttributeValue<string>($"fwp_{Days[i]}break"),
                    ExpectedHours = cfg.GetAttributeValue<decimal?>($"fwp_{Days[i]}expectedhours")
                });
            return settings;
        }

        /// <summary>Saves everything on the HR screen through fwp_SetLeaveSettings (which checks the caller is HR).</summary>
        public void SaveLeaveSettings(LeaveSettings settings)
        {
            if (!_connector.Connect())
                throw new InvalidOperationException("Not connected to Dynamics.");
            var request = new OrganizationRequest("fwp_SetLeaveSettings")
            {
                ["EmployeeId"] = settings.EmployeeId,
                ["ApproverId"] = settings.ApproverId ?? Guid.Empty,
                ["AdditionalApproverId"] = settings.AdditionalApproverId ?? Guid.Empty
            };
            if (settings.BaseEntitlement.HasValue)
                request["BaseEntitlement"] = settings.BaseEntitlement.Value;
            foreach (var day in settings.Week)
            {
                request[day.Day + "Start"] = day.Start ?? "";
                request[day.Day + "Finish"] = day.Finish ?? "";
                request[day.Day + "Break"] = day.Break ?? "";
            }
            _connector._orgService.Execute(request);
        }

        public List<LeaveEntitlement> GetEntitlements(Guid employeeId)
        {
            if (!_connector.Connect())
                return new List<LeaveEntitlement>();
            var q = new QueryExpression("fwp_leaveentitlement")
            {
                ColumnSet = new ColumnSet("fwp_yearstart", "fwp_entitlementdays", "fwp_carriedforwarddays", "fwp_adjustmentdays",
                                          "fwp_carryforwardoverride", "fwp_carriedtonextyeardays"),
                Orders = { new OrderExpression("fwp_yearstart", OrderType.Descending) }
            };
            q.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities.Select(e => new LeaveEntitlement
            {
                Id = e.Id,
                Year = e.GetAttributeValue<DateTime>("fwp_yearstart").Year,
                BaseDays = e.GetAttributeValue<decimal?>("fwp_entitlementdays") ?? 0m,
                CarriedForwardDays = e.GetAttributeValue<decimal?>("fwp_carriedforwarddays") ?? 0m,
                AdjustmentDays = e.GetAttributeValue<decimal?>("fwp_adjustmentdays") ?? 0m,
                CarryForwardOverride = e.GetAttributeValue<decimal?>("fwp_carryforwardoverride"),
                CarriedToNextYearDays = e.GetAttributeValue<decimal?>("fwp_carriedtonextyeardays") ?? 0m
            }).ToList();
        }

        /// <summary>
        /// Creates or updates one year's entitlement. The plug-in names it, gives it to the employee and refuses a second row for
        /// the same year. "Carried to next year" belongs to the year-end process and isn't written here.
        /// </summary>
        public void SaveEntitlement(Guid employeeId, LeaveEntitlement ent)
        {
            if (!_connector.Connect())
                throw new InvalidOperationException("Not connected to Dynamics.");
            var e = new Entity("fwp_leaveentitlement")
            {
                ["fwp_entitlementdays"] = ent.BaseDays,
                ["fwp_carriedforwarddays"] = ent.CarriedForwardDays,
                ["fwp_adjustmentdays"] = ent.AdjustmentDays,
                ["fwp_carryforwardoverride"] = ent.CarryForwardOverride
            };
            if (ent.Id == Guid.Empty)
            {
                e["fwp_employee"] = new EntityReference("systemuser", employeeId);
                e["fwp_yearstart"] = DateTime.SpecifyKind(new DateTime(ent.Year, 1, 1), DateTimeKind.Unspecified);
                ent.Id = _connector._orgService.Create(e);
            }
            else
            {
                e.Id = ent.Id;
                _connector._orgService.Update(e);
            }
        }

        /// <summary>Everyone with a colleague configuration - who HR can see in the team view.</summary>
        public List<LeavePerson> GetEveryone()
        {
            if (!_connector.Connect())
                return new List<LeavePerson>();
            var q = new QueryExpression("fwp_colleagueconfiguration") { ColumnSet = new ColumnSet("fwp_colleague") };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.NotNull);
            return People(_connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)));
        }

        private static List<LeavePerson> People(EntityCollection configs) =>
            configs.Entities.Select(e => e.GetAttributeValue<EntityReference>("fwp_colleague"))
                .Where(r => r != null)
                .GroupBy(r => r.Id)
                .Select(g => new LeavePerson { Id = g.Key, Name = g.First().Name })
                .OrderBy(p => p.Name)
                .ToList();

        /// <summary>
        /// Requests waiting for a decision (new requests and requests to cancel approved leave). HR see everyone's;
        /// approvers see their people's (shared with them by the plug-in).
        /// </summary>
        public List<LeaveRequest> GetRequestsAwaitingDecision(ApproverScope scope)
        {
            if (!_connector.Connect())
                return new List<LeaveRequest>();

            var q = new QueryExpression("fwp_leaverequest")
            {
                ColumnSet = new ColumnSet(RequestColumns),
                Orders = { new OrderExpression("fwp_startdate", OrderType.Ascending) }
            };
            q.Criteria.AddCondition("fwp_status", ConditionOperator.In,
                (int)LeaveRequestStatus.Pending, (int)LeaveRequestStatus.CancellationRequested);
            if (!scope.IsLeaveAdmin)
            {
                if (scope.MyPeople.Count == 0)
                    return new List<LeaveRequest>();
                q.Criteria.AddCondition("fwp_employee", ConditionOperator.In, scope.MyPeople.Select(p => (object)p.Id).ToArray());
            }

            return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q))
                .Entities.Select(LeaveRequest.FromEntity).ToList();
        }

        /// <summary>
        /// Everyone's pending and booked leave overlapping the window ("who's off", M3), from the fwp_GetTeamLeave Custom API -
        /// line managers can't read other teams' requests directly. The type is only given for your own people (or to HR)
        /// and for non-absence types such as Working From Home.
        /// </summary>
        public List<LeaveRequest> GetTeamLeave(DateTime from, DateTime to)
        {
            if (!_connector.Connect())
                return new List<LeaveRequest>();
            var request = new OrganizationRequest("fwp_GetTeamLeave") { ["FromDate"] = from.Date, ["ToDate"] = to.Date };
            var response = _connector.ExecuteWithRetry(() => _connector._orgService.Execute(request));
            return ((EntityCollection)response["Leave"]).Entities.Select(LeaveRequest.FromEntity).ToList();
        }

        /// <summary>
        /// Records an approver's decision. Approving creates the time entries and cancelling removes them -
        /// that happens server-side in LeaveRequestPlugin.
        /// </summary>
        public void Decide(LeaveRequest request, LeaveRequestStatus newStatus, string comment, Guid approverId)
        {
            if (!_connector.Connect())
                throw new InvalidOperationException("Not connected to Dynamics.");

            var e = new Entity("fwp_leaverequest", request.Id)
            {
                ["fwp_status"] = new OptionSetValue((int)newStatus),
                ["fwp_approver"] = new EntityReference("systemuser", approverId)
            };
            if (!string.IsNullOrWhiteSpace(comment))
                e["fwp_approvercomments"] = comment.Trim();
            _connector._orgService.Update(e);
        }

        /// <summary>
        /// Same pattern the server uses (LeaveTimeEntryService): working days from colleague configuration, hours per leave
        /// day from the fwp_LeaveDayHours setting (8.5h Mon-Thu, 8h Fri), so the preview matches what is stored.
        /// </summary>
        public WorkingDay[] GetWorkingPattern(Guid employeeId)
        {
            var leaveDayHours = GetLeaveDayHours();
            if (!_connector.Connect())
                return LeaveDayCalculator.BuildPattern(leaveDayHours, null);

            var q = new QueryExpression("fwp_colleagueconfiguration")
            {
                ColumnSet = new ColumnSet(Days.SelectMany(d => new[] { $"fwp_{d}expectedhours", $"fwp_{d}start", $"fwp_{d}finish" }).ToArray()),
                TopCount = 1
            };
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var cfg = _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities.FirstOrDefault();
            if (cfg == null)
                return LeaveDayCalculator.BuildPattern(leaveDayHours, null);

            return LeaveDayCalculator.BuildPattern(leaveDayHours,
                Days.Select(d => cfg.GetAttributeValue<decimal?>($"fwp_{d}expectedhours")).ToArray(),
                Days.Select(d => cfg.GetAttributeValue<string>($"fwp_{d}start")).ToArray(),
                Days.Select(d => cfg.GetAttributeValue<string>($"fwp_{d}finish")).ToArray());
        }

        private decimal[] GetLeaveDayHours()
        {
            try
            {
                if (!_connector.Connect())
                    return LeaveDayCalculator.ParseLeaveDayHours(null);
                var q = new QueryExpression("environmentvariabledefinition") { ColumnSet = new ColumnSet("defaultvalue") };
                q.Criteria.AddCondition("schemaname", ConditionOperator.Equal, "fwp_LeaveDayHours");
                var link = q.AddLink("environmentvariablevalue", "environmentvariabledefinitionid", "environmentvariabledefinitionid", JoinOperator.LeftOuter);
                link.Columns = new ColumnSet("value");
                link.EntityAlias = "v";
                var def = _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities.FirstOrDefault();
                var value = def?.GetAttributeValue<AliasedValue>("v.value")?.Value as string;
                return LeaveDayCalculator.ParseLeaveDayHours(!string.IsNullOrEmpty(value) ? value : def?.GetAttributeValue<string>("defaultvalue"));
            }
            catch (Exception ex)
            {
                // Staff may not be able to read settings - the standard week is the agreed value anyway
                System.Diagnostics.Debug.WriteLine($"LeaveService.GetLeaveDayHours: {ex.Message}");
                return LeaveDayCalculator.ParseLeaveDayHours(null);
            }
        }

        public HashSet<DateTime> GetBankHolidays(DateTime from, DateTime to)
        {
            if (!_connector.Connect())
                return new HashSet<DateTime>();

            var q = new QueryExpression("fwp_publicholiday") { ColumnSet = new ColumnSet("fwp_date") };
            q.Criteria.AddCondition("fwp_date", ConditionOperator.OnOrAfter, from.Date);
            q.Criteria.AddCondition("fwp_date", ConditionOperator.OnOrBefore, to.Date);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            return new HashSet<DateTime>(_connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q))
                .Entities.Select(e => e.GetAttributeValue<DateTime>("fwp_date").Date));
        }

        /// <summary>
        /// Balance in DAYS for one leave (calendar) year, from the fwp_GetLeaveBalance Custom API - the same rules for
        /// your own balance and for an approver looking at one of their people (who can't read the entitlement directly).
        /// Pending requests count against the balance (HR decision); only types that deduct from entitlement count.
        /// </summary>
        public LeaveBalance GetBalance(Guid employeeId, int year)
        {
            var balance = new LeaveBalance();
            if (!_connector.Connect())
                return balance;

            var request = new OrganizationRequest("fwp_GetLeaveBalance") { ["EmployeeId"] = employeeId, ["Year"] = year };
            var r = _connector.ExecuteWithRetry(() => _connector._orgService.Execute(request));
            if (!(r["HasEntitlement"] is bool has) || !has)
                return balance;

            balance.YearStart = new DateTime(year, 1, 1);
            balance.BaseDays = (decimal)r["BaseDays"];
            balance.CarriedForwardDays = (decimal)r["CarriedForwardDays"];
            balance.AdjustmentDays = (decimal)r["AdjustmentDays"];
            balance.CarriedToNextYearDays = (decimal)r["CarriedToNextYearDays"];
            balance.EntitlementDays = balance.BaseDays + balance.CarriedForwardDays + balance.AdjustmentDays;
            balance.TakenDays = (decimal)r["TakenDays"];
            balance.BookedDays = (decimal)r["BookedDays"];
            balance.PendingDays = (decimal)r["PendingDays"];
            return balance;
        }
    }
}
