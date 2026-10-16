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

        private static readonly string[] RequestColumns =
        {
            "fwp_employee", "fwp_leavetype", "fwp_startdate", "fwp_enddate", "fwp_starttime", "fwp_endtime",
            "fwp_hours", "fwp_days", "fwp_status", "fwp_approver", "fwp_requestercomments", "fwp_approvercomments", "createdon"
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
                ColumnSet = new ColumnSet("fwp_name", "fwp_deductsfromentitlement", "fwp_isabsence", "fwp_sortorder"),
                Orders = { new OrderExpression("fwp_sortorder", OrderType.Ascending), new OrderExpression("fwp_name", OrderType.Ascending) }
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities
                .Select(e => new LeaveType
                {
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("fwp_name"),
                    DeductsFromEntitlement = e.GetAttributeValue<bool?>("fwp_deductsfromentitlement") ?? true,
                    IsAbsence = e.GetAttributeValue<bool?>("fwp_isabsence") ?? true
                }).ToList();
        }

        /// <summary>Submits a new request. The server works out the hours, approver and status, and rejects overlaps.</summary>
        public Guid SubmitRequest(LeaveRequest request)
        {
            if (!_connector.Connect())
                throw new InvalidOperationException("Not connected to Dynamics.");
            return _connector._orgService.Create(request.ToEntity());
        }

        /// <summary>Pending requests are cancelled outright; approved ones go back to the approver to confirm.</summary>
        public void CancelRequest(LeaveRequest request)
        {
            if (!_connector.Connect())
                throw new InvalidOperationException("Not connected to Dynamics.");

            var status = request.Status == LeaveRequestStatus.Pending ? LeaveRequestStatus.Cancelled : LeaveRequestStatus.CancellationRequested;
            var e = new Entity("fwp_leaverequest", request.Id) { ["fwp_status"] = new OptionSetValue((int)status) };
            _connector._orgService.Update(e);
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

        /// <summary>Everyone's requests waiting for a decision: new requests and requests to cancel approved leave.</summary>
        public List<LeaveRequest> GetRequestsAwaitingDecision()
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

            return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q))
                .Entities.Select(LeaveRequest.FromEntity).ToList();
        }

        /// <summary>Approved leave overlapping the given window - who's off.</summary>
        public List<LeaveRequest> GetApprovedLeave(DateTime from, DateTime to)
        {
            if (!_connector.Connect())
                return new List<LeaveRequest>();

            var q = new QueryExpression("fwp_leaverequest")
            {
                ColumnSet = new ColumnSet(RequestColumns),
                Orders = { new OrderExpression("fwp_startdate", OrderType.Ascending) }
            };
            q.Criteria.AddCondition("fwp_status", ConditionOperator.In,
                (int)LeaveRequestStatus.Approved, (int)LeaveRequestStatus.CancellationRequested);
            q.Criteria.AddCondition("fwp_startdate", ConditionOperator.OnOrBefore, to.Date);
            q.Criteria.AddCondition("fwp_enddate", ConditionOperator.OnOrAfter, from.Date);

            return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q))
                .Entities.Select(LeaveRequest.FromEntity).ToList();
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
        /// Balance in DAYS for the leave year (calendar year) containing <paramref name="onDate"/>. Pending requests count as
        /// taken (HR decision); only types that deduct from entitlement count.
        /// </summary>
        public LeaveBalance GetBalance(Guid employeeId, DateTime onDate, IEnumerable<LeaveRequest> requests, IList<LeaveType> types)
        {
            var balance = new LeaveBalance();
            if (!_connector.Connect())
                return balance;

            var yearStart = new DateTime(onDate.Year, 1, 1);
            var q = new QueryExpression("fwp_leaveentitlement")
            {
                ColumnSet = new ColumnSet("fwp_yearstart", "fwp_entitlementdays", "fwp_carriedforwarddays", "fwp_adjustmentdays"),
                TopCount = 1
            };
            q.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("fwp_yearstart", ConditionOperator.On, yearStart);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var ent = _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities.FirstOrDefault();
            if (ent == null)
                return balance;

            balance.YearStart = yearStart;
            balance.EntitlementDays = (ent.GetAttributeValue<decimal?>("fwp_entitlementdays") ?? 0m)
                + (ent.GetAttributeValue<decimal?>("fwp_carriedforwarddays") ?? 0m)
                + (ent.GetAttributeValue<decimal?>("fwp_adjustmentdays") ?? 0m);

            var deducting = new HashSet<Guid>(types.Where(t => t.DeductsFromEntitlement).Select(t => t.Id));
            foreach (var r in requests.Where(r => r.StartDate.Year == onDate.Year
                                                  && (!r.LeaveTypeId.HasValue || deducting.Contains(r.LeaveTypeId.Value))))
            {
                if (r.Status == LeaveRequestStatus.Approved || r.Status == LeaveRequestStatus.CancellationRequested)
                {
                    if (r.EndDate.Date < DateTime.Today) balance.TakenDays += r.Days;
                    else balance.BookedDays += r.Days;
                }
                else if (r.Status == LeaveRequestStatus.Pending)
                {
                    balance.BookedDays += r.Days;
                    balance.PendingDays += r.Days;
                }
            }
            return balance;
        }
    }
}
