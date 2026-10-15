using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.Leave.Plugins
{
    /// <summary>Per-environment settings, held in Dataverse environment variables (solution FWPLeaveTimeEntries).</summary>
    public class LeaveSettings
    {
        public const string LeaveProjectVar = "fwp_LeaveProjectId";
        public const string BankHolidayProjectVar = "fwp_BankHolidayProjectId";
        public const string DefaultDayHoursVar = "fwp_LeaveDefaultDayHours";
        public const string LeaveLabelVar = "fwp_LeaveEntryLabel";
        public const string BankHolidayLabelVar = "fwp_BankHolidayEntryLabel";
        public const string LeaveDayHoursVar = "fwp_LeaveDayHours";

        public Guid LeaveProjectId { get; private set; }
        public Guid BankHolidayProjectId { get; private set; }
        public decimal DefaultDayHours { get; private set; } = LeaveDayCalculator.DefaultDayHours;
        public string LeaveLabel { get; private set; } = "Approved leave";
        public string BankHolidayLabel { get; private set; } = "Bank holiday";
        /// <summary>Hours a day of leave records, Monday first.</summary>
        public decimal[] LeaveDayHours { get; private set; } = (decimal[])LeaveDayCalculator.StandardLeaveDayHours.Clone();

        public static LeaveSettings Load(IOrganizationService svc)
        {
            var values = ReadVariables(svc);
            var s = new LeaveSettings();

            if (values.TryGetValue(LeaveProjectVar, out var v) && Guid.TryParse(v, out var g)) s.LeaveProjectId = g;
            if (values.TryGetValue(BankHolidayProjectVar, out v) && Guid.TryParse(v, out g)) s.BankHolidayProjectId = g;
            if (values.TryGetValue(DefaultDayHoursVar, out v) &&
                decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var h) && h > 0) s.DefaultDayHours = h;
            if (values.TryGetValue(LeaveLabelVar, out v) && !string.IsNullOrWhiteSpace(v)) s.LeaveLabel = v;
            if (values.TryGetValue(BankHolidayLabelVar, out v) && !string.IsNullOrWhiteSpace(v)) s.BankHolidayLabel = v;
            if (values.TryGetValue(LeaveDayHoursVar, out v)) s.LeaveDayHours = LeaveDayCalculator.ParseLeaveDayHours(v);
            return s;
        }

        // Current value if set, otherwise the definition's default value
        private static Dictionary<string, string> ReadVariables(IOrganizationService svc)
        {
            var q = new QueryExpression("environmentvariabledefinition") { ColumnSet = new ColumnSet("schemaname", "defaultvalue") };
            q.Criteria.AddCondition("schemaname", ConditionOperator.In,
                LeaveProjectVar, BankHolidayProjectVar, DefaultDayHoursVar, LeaveLabelVar, BankHolidayLabelVar, LeaveDayHoursVar);
            var val = q.AddLink("environmentvariablevalue", "environmentvariabledefinitionid", "environmentvariabledefinitionid", JoinOperator.LeftOuter);
            val.Columns = new ColumnSet("value");
            val.EntityAlias = "v";

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in svc.RetrieveMultiple(q).Entities)
            {
                var current = e.GetAttributeValue<AliasedValue>("v.value")?.Value as string;
                result[e.GetAttributeValue<string>("schemaname")] = !string.IsNullOrEmpty(current) ? current : e.GetAttributeValue<string>("defaultvalue");
            }
            return result;
        }
    }
}
