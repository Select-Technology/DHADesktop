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
                ColumnSet = new ColumnSet("fwp_employee", "fwp_leavetype", "fwp_startdate", "fwp_enddate", "fwp_starttime", "fwp_endtime",
                    "fwp_hours", "fwp_status", "fwp_approver", "fwp_requestercomments", "fwp_approvercomments"),
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
                ColumnSet = new ColumnSet("fwp_name", "fwp_deductsfromentitlement"),
                Orders = { new OrderExpression("fwp_name", OrderType.Ascending) }
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            return _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities
                .Select(e => new LeaveType
                {
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("fwp_name"),
                    DeductsFromEntitlement = e.GetAttributeValue<bool?>("fwp_deductsfromentitlement") ?? true
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

        public WorkingDay[] GetWorkingPattern(Guid employeeId)
        {
            var pattern = LeaveDayCalculator.DefaultPattern();
            if (!_connector.Connect())
                return pattern;

            var q = new QueryExpression("fwp_colleagueconfiguration")
            {
                ColumnSet = new ColumnSet(Days.SelectMany(d => new[] { $"fwp_{d}expectedhours", $"fwp_{d}start", $"fwp_{d}finish" }).ToArray()),
                TopCount = 1
            };
            q.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var cfg = _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities.FirstOrDefault();
            if (cfg == null)
                return pattern;

            for (int i = 0; i < Days.Length; i++)
            {
                var hours = cfg.GetAttributeValue<decimal?>($"fwp_{Days[i]}expectedhours");
                if (!hours.HasValue)
                    continue;
                pattern[i] = new WorkingDay
                {
                    ExpectedHours = hours.Value,
                    Start = LeaveDayCalculator.ParseTime(cfg.GetAttributeValue<string>($"fwp_{Days[i]}start"), false) ?? new TimeSpan(9, 0, 0),
                    Finish = LeaveDayCalculator.ParseTime(cfg.GetAttributeValue<string>($"fwp_{Days[i]}finish"), true) ?? new TimeSpan(17, 30, 0)
                };
            }
            return pattern;
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

        /// <summary>Entitlement for the leave year containing <paramref name="onDate"/>, less taken and booked leave.</summary>
        public LeaveBalance GetBalance(Guid employeeId, DateTime onDate, IEnumerable<LeaveRequest> requests, IList<LeaveType> types)
        {
            var balance = new LeaveBalance();
            if (!_connector.Connect())
                return balance;

            var q = new QueryExpression("fwp_leaveentitlement")
            {
                ColumnSet = new ColumnSet("fwp_yearstart", "fwp_entitlementhours", "fwp_carriedforwardhours", "fwp_purchasedhours", "fwp_adjustmenthours"),
                TopCount = 1,
                Orders = { new OrderExpression("fwp_yearstart", OrderType.Descending) }
            };
            q.Criteria.AddCondition("fwp_employee", ConditionOperator.Equal, employeeId);
            q.Criteria.AddCondition("fwp_yearstart", ConditionOperator.OnOrBefore, onDate.Date);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var ent = _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(q)).Entities.FirstOrDefault();
            if (ent == null)
                return balance;

            balance.YearStart = ent.GetAttributeValue<DateTime>("fwp_yearstart").Date;
            balance.EntitlementHours = (ent.GetAttributeValue<decimal?>("fwp_entitlementhours") ?? 0m)
                + (ent.GetAttributeValue<decimal?>("fwp_carriedforwardhours") ?? 0m)
                + (ent.GetAttributeValue<decimal?>("fwp_purchasedhours") ?? 0m)
                + (ent.GetAttributeValue<decimal?>("fwp_adjustmenthours") ?? 0m);

            var yearEnd = balance.YearStart.AddYears(1);
            var deducting = new HashSet<Guid>(types.Where(t => t.DeductsFromEntitlement).Select(t => t.Id));
            foreach (var r in requests.Where(r => r.StartDate >= balance.YearStart && r.StartDate < yearEnd
                                                  && (!r.LeaveTypeId.HasValue || deducting.Contains(r.LeaveTypeId.Value))))
            {
                if (r.Status == LeaveRequestStatus.Approved || r.Status == LeaveRequestStatus.CancellationRequested)
                {
                    if (r.EndDate.Date < DateTime.Today) balance.TakenHours += r.Hours;
                    else balance.BookedHours += r.Hours;
                }
                else if (r.Status == LeaveRequestStatus.Pending)
                {
                    balance.PendingHours += r.Hours;
                }
            }
            return balance;
        }
    }
}
