using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;

namespace DHA.Leave.Plugins
{
    /// <summary>Shared plumbing for the leave Custom API plug-ins. Writes run as the calling user.</summary>
    public abstract class LeaveApiPluginBase : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var svc = factory.CreateOrganizationService(context.UserId);

            try
            {
                Run(context, new LeaveTimeEntryService(svc, trace));
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

        protected abstract void Run(IPluginExecutionContext context, LeaveTimeEntryService service);

        protected static T Input<T>(IPluginExecutionContext context, string name)
        {
            return context.InputParameters.TryGetValue(name, out var v) && v is T t ? t : default(T);
        }

        /// <summary>Optional DateTime parameters arrive as DateTime.MinValue when omitted, not null.</summary>
        protected static DateTime? InputDate(IPluginExecutionContext context, string name)
        {
            var value = Input<DateTime?>(context, name);
            return value.HasValue && value.Value.Year > 1900 ? value : null;
        }

        protected static TimeSpan? InputTime(IPluginExecutionContext context, string name)
        {
            var text = Input<string>(context, name);
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var ts))
                return ts;
            throw new InvalidPluginExecutionException($"{name} must be a time in HH:mm format, got '{text}'.");
        }
    }

    /// <summary>
    /// Custom API fwp_RecordLeave - creates (or replaces) the time entries for one approved leave request.
    /// In: UserId | UserEmail, StartDate, EndDate, StartTime?, EndTime? ("HH:mm"), LeaveKind? ("Leave" | "BankHoliday"), SourceKey.
    /// Out: EntriesCreated, EntriesRemoved, TotalHours.
    /// </summary>
    public class RecordLeavePlugin : LeaveApiPluginBase
    {
        protected override void Run(IPluginExecutionContext context, LeaveTimeEntryService service)
        {
            var userId = service.ResolveUser(Input<Guid?>(context, "UserId"), Input<string>(context, "UserEmail"));
            var start = InputDate(context, "StartDate") ?? throw new InvalidPluginExecutionException("StartDate is required.");
            var end = InputDate(context, "EndDate") ?? start;

            var result = service.RecordLeave(userId, start, end,
                InputTime(context, "StartTime"), InputTime(context, "EndTime"),
                Input<string>(context, "LeaveKind") ?? LeaveTimeEntryService.KindLeave,
                Input<string>(context, "SourceKey"));

            context.OutputParameters["EntriesCreated"] = result.Created;
            context.OutputParameters["EntriesRemoved"] = result.Removed;
            context.OutputParameters["TotalHours"] = result.TotalHours;
        }
    }

    /// <summary>Custom API fwp_RemoveLeave - removes the time entries for a cancelled/rejected request. In: SourceKey. Out: EntriesRemoved.</summary>
    public class RemoveLeavePlugin : LeaveApiPluginBase
    {
        protected override void Run(IPluginExecutionContext context, LeaveTimeEntryService service)
        {
            context.OutputParameters["EntriesRemoved"] = service.RemoveLeave(Input<string>(context, "SourceKey"));
        }
    }

    /// <summary>
    /// Custom API fwp_RecordBankHolidays - bank holiday entries for every active colleague for holidays in a range.
    /// In: FromDate, ToDate. Out: EntriesCreated, EntriesRemoved, TotalHours.
    /// </summary>
    public class RecordBankHolidaysPlugin : LeaveApiPluginBase
    {
        protected override void Run(IPluginExecutionContext context, LeaveTimeEntryService service)
        {
            var from = InputDate(context, "FromDate") ?? DateTime.UtcNow.Date;
            var to = InputDate(context, "ToDate") ?? from.AddDays(31);
            var result = service.RecordBankHolidays(from, to);

            context.OutputParameters["EntriesCreated"] = result.Created;
            context.OutputParameters["EntriesRemoved"] = result.Removed;
            context.OutputParameters["TotalHours"] = result.TotalHours;
        }
    }
}
