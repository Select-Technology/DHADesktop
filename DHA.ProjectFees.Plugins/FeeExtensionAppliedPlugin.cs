using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.ProjectFees.Plugins
{
    /// <summary>
    /// Keeps a project's fee in step with its fee extensions:
    ///     DHA Fee (isc_projectamount) = original fee (fwp_originalprojectfee) + active fee extensions
    ///
    /// REGISTER: synchronous, POST-OPERATION on fwp_feeextension
    ///   - Create
    ///   - Update, filtering attributes = fwp_amount, statecode, fwp_project; Pre Image "PreImage" with fwp_project
    ///   - Delete, Pre Image "PreImage" with fwp_project
    ///
    /// Every change recalculates the affected project(s) from the active extensions rather than adding or
    /// subtracting, so amount corrections, deactivating, reactivating, deleting and moving an extension to another
    /// project all leave the fee right. Also sets isc_feeextension (the total) and fwp_lastfeeextensiondate /
    /// fwp_previousfeeextensiondate (created on of the two newest active extensions).
    /// A project with no original fee (created before fee extensions existed) gets DHA Fee less the extensions
    /// already counted in it, i.e. the fee before this change.
    /// Runs as the user making the change, so it only succeeds if they may update the project.
    /// </summary>
    public class FeeExtensionAppliedPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            if (context.PrimaryEntityName != ProjectFeeFields.FeeExtension)
            {
                return;
            }

            var target = context.InputParameters.Contains("Target") ? context.InputParameters["Target"] as Entity : null;
            var before = context.PreEntityImages.Contains("PreImage") ? context.PreEntityImages["PreImage"] : null;
            var oldProject = before?.GetAttributeValue<EntityReference>(ProjectFeeFields.ExtensionProject);
            EntityReference newProject;

            switch (context.MessageName)
            {
                case "Create":
                    newProject = target?.GetAttributeValue<EntityReference>(ProjectFeeFields.ExtensionProject);
                    if (newProject == null)
                    {
                        throw new InvalidPluginExecutionException("A fee extension must be linked to a project before it can be created.");
                    }
                    break;
                case "Update":
                    newProject = target != null && target.Contains(ProjectFeeFields.ExtensionProject)
                        ? target.GetAttributeValue<EntityReference>(ProjectFeeFields.ExtensionProject)
                        : oldProject;
                    break;
                case "Delete":
                    newProject = null;
                    break;
                default:
                    return;
            }

            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            foreach (var projectId in new[] { oldProject?.Id, newProject?.Id }.Where(id => id.HasValue).Select(id => id.Value).Distinct())
            {
                Recalculate(service, tracing, projectId);
            }
        }

        public static void Recalculate(IOrganizationService service, ITracingService tracing, Guid projectId)
        {
            var project = service.Retrieve(ProjectFeeFields.Project, projectId,
                new ColumnSet(ProjectFeeFields.DhaFee, ProjectFeeFields.OriginalFee, ProjectFeeFields.ExtensionsTotal));
            var currentFee = project.GetAttributeValue<Money>(ProjectFeeFields.DhaFee)?.Value ?? 0m;
            var original = project.GetAttributeValue<Money>(ProjectFeeFields.OriginalFee)?.Value
                ?? currentFee - (project.GetAttributeValue<Money>(ProjectFeeFields.ExtensionsTotal)?.Value ?? 0m);

            var total = SumActiveExtensions(service, projectId);
            var dates = NewestExtensionDates(service, projectId);

            service.Update(new Entity(ProjectFeeFields.Project, projectId)
            {
                [ProjectFeeFields.OriginalFee] = new Money(original),
                [ProjectFeeFields.DhaFee] = new Money(original + total),
                [ProjectFeeFields.ExtensionsTotal] = new Money(total),
                [ProjectFeeFields.LastExtensionDate] = dates.Length > 0 ? (object)dates[0] : null,
                [ProjectFeeFields.PreviousExtensionDate] = dates.Length > 1 ? (object)dates[1] : null
            });
            tracing.Trace("Project {0}: DHA Fee {1} -> {2} (original {3} + extensions {4})", projectId, currentFee, original + total, original, total);
        }

        public static decimal SumActiveExtensions(IOrganizationService service, Guid projectId)
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
