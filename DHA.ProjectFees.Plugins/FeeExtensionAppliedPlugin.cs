using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.ProjectFees.Plugins
{
    /// <summary>
    /// Applies a new fee extension to its project.
    ///
    /// REGISTER: synchronous, POST-OPERATION, Create of fwp_feeextension.
    ///
    ///   * fwp_originalprojectfee := current DHA Fee, if it was never set (projects created before fee extensions)
    ///   * isc_projectamount (DHA Fee) += the extension amount
    ///   * isc_feeextension := sum of the project's active fee extensions
    ///   * fwp_lastfeeextensiondate / fwp_previousfeeextensiondate := created on of the two newest active extensions
    /// Runs as the user creating the extension, so it only succeeds if they may update the project.
    /// Deactivating or deleting an extension does not reverse it.
    /// </summary>
    public class FeeExtensionAppliedPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            if (context.MessageName != "Create"
                || context.PrimaryEntityName != ProjectFeeFields.FeeExtension
                || !context.InputParameters.Contains("Target")
                || !(context.InputParameters["Target"] is Entity extension))
            {
                return;
            }

            var projectRef = extension.GetAttributeValue<EntityReference>(ProjectFeeFields.ExtensionProject);
            if (projectRef == null)
            {
                throw new InvalidPluginExecutionException("A fee extension must be linked to a project before it can be created.");
            }
            var amount = extension.GetAttributeValue<Money>(ProjectFeeFields.ExtensionAmount)?.Value ?? 0m;

            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            var project = service.Retrieve(ProjectFeeFields.Project, projectRef.Id,
                new ColumnSet(ProjectFeeFields.DhaFee, ProjectFeeFields.OriginalFee));
            var currentFee = project.GetAttributeValue<Money>(ProjectFeeFields.DhaFee)?.Value ?? 0m;

            var update = new Entity(ProjectFeeFields.Project, projectRef.Id)
            {
                [ProjectFeeFields.DhaFee] = new Money(currentFee + amount),
                [ProjectFeeFields.ExtensionsTotal] = new Money(SumActiveExtensions(service, projectRef.Id))
            };
            if (project.GetAttributeValue<Money>(ProjectFeeFields.OriginalFee) == null)
            {
                update[ProjectFeeFields.OriginalFee] = new Money(currentFee);
            }

            var dates = NewestExtensionDates(service, projectRef.Id);
            update[ProjectFeeFields.LastExtensionDate] = dates.Length > 0 ? (object)dates[0] : null;
            update[ProjectFeeFields.PreviousExtensionDate] = dates.Length > 1 ? (object)dates[1] : null;

            service.Update(update);
            tracing.Trace("Project {0}: DHA Fee {1} -> {2} (extension {3})", projectRef.Id, currentFee, currentFee + amount, amount);
        }

        private static decimal SumActiveExtensions(IOrganizationService service, Guid projectId)
        {
            var fetch = $@"<fetch aggregate='true'>
  <entity name='{ProjectFeeFields.FeeExtension}'>
    <attribute name='{ProjectFeeFields.ExtensionAmount}' alias='total' aggregate='sum' />
    <filter>
      <condition attribute='{ProjectFeeFields.ExtensionProject}' operator='eq' value='{projectId}' />
      <condition attribute='statecode' operator='eq' value='0' />
    </filter>
  </entity>
</fetch>";
            var row = service.RetrieveMultiple(new FetchExpression(fetch)).Entities.FirstOrDefault();
            return (row?.GetAttributeValue<AliasedValue>("total")?.Value as Money)?.Value ?? 0m;
        }

        private static DateTime[] NewestExtensionDates(IOrganizationService service, Guid projectId)
        {
            var query = new QueryExpression(ProjectFeeFields.FeeExtension) { ColumnSet = new ColumnSet("createdon"), TopCount = 2 };
            query.Criteria.AddCondition(ProjectFeeFields.ExtensionProject, ConditionOperator.Equal, projectId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.AddOrder("createdon", OrderType.Descending);
            return service.RetrieveMultiple(query).Entities
                .Select(e => e.GetAttributeValue<DateTime?>("createdon"))
                .Where(d => d.HasValue)
                .Select(d => d.Value)
                .ToArray();
        }
    }
}
