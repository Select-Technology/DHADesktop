using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.ProjectFees.Plugins
{
    /// <summary>
    /// Keeps fwp_originalprojectfee ("Original project fee - as agreed at project start") consistent with the DHA Fee
    /// (DHA Fee = original fee + active fee extensions) and guards the DHA Fee once a project has fee extensions.
    ///
    /// REGISTER: synchronous, PRE-OPERATION on msdyn_project
    ///   - Create
    ///   - Update, filtering attributes = isc_projectamount, Pre Image "PreImage" with isc_projectamount
    ///
    ///   * Create: original fee := DHA Fee (when the DHA Fee is supplied).
    ///   * A changed DHA Fee from a person or flow (Depth 1):
    ///       - no fee extensions yet       -> original fee follows it (corrections before any extension)
    ///       - fee extensions, System Administrator -> allowed as a correction; original fee := DHA Fee - extensions,
    ///                                         so the correction survives the next extension change
    ///       - fee extensions, anyone else -> blocked; Finance should raise a fee extension instead
    ///   * Saves that resend an unchanged DHA Fee, and FeeExtensionAppliedPlugin's own update (Depth > 1), are left alone.
    /// Setting values on the Target in pre-operation saves them with the same write, so there is no second Update.
    /// </summary>
    public class ProjectFeePlugin : IPlugin
    {
        public const string ManualChangeMessage =
            "This project has fee extensions, so its DHA Fee can't be changed by hand. To increase the fee, please ask " +
            "Finance to raise a fee extension. A System Administrator can make a manual correction.";

        // System Administrator role template, the same in every environment
        private static readonly Guid SystemAdministratorTemplate = new Guid("627090FF-40A3-4053-8790-584EDC5BE201");

        public void Execute(IServiceProvider serviceProvider)
        {
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            if (context.PrimaryEntityName != ProjectFeeFields.Project
                || !context.InputParameters.Contains("Target")
                || !(context.InputParameters["Target"] is Entity target)
                || !target.Contains(ProjectFeeFields.DhaFee))
            {
                return;
            }

            var dhaFee = target.GetAttributeValue<Money>(ProjectFeeFields.DhaFee);

            if (context.MessageName == "Create")
            {
                if (dhaFee != null && !target.Contains(ProjectFeeFields.OriginalFee))
                {
                    target[ProjectFeeFields.OriginalFee] = new Money(dhaFee.Value);
                    tracing.Trace("Create: original fee = {0}", dhaFee.Value);
                }
                return;
            }

            if (context.MessageName != "Update" || context.Depth > 1)
            {
                return;
            }

            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(null);

            // Saves and flows that resend an unchanged DHA Fee are not edits. The image leaves out a column the caller
            // can't read (DHA Fee is column-secured in dev), so read it as the system then.
            var image = context.PreEntityImages.Contains("PreImage") ? context.PreEntityImages["PreImage"] : null;
            var before = image != null && image.Contains(ProjectFeeFields.DhaFee)
                ? image.GetAttributeValue<Money>(ProjectFeeFields.DhaFee)
                : service.Retrieve(ProjectFeeFields.Project, target.Id, new ColumnSet(ProjectFeeFields.DhaFee))
                    .GetAttributeValue<Money>(ProjectFeeFields.DhaFee);
            if (before?.Value == dhaFee?.Value)
            {
                return;
            }

            var query = new QueryExpression(ProjectFeeFields.FeeExtension) { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition(ProjectFeeFields.ExtensionProject, ConditionOperator.Equal, target.Id);
            if (service.RetrieveMultiple(query).Entities.Count == 0)
            {
                target[ProjectFeeFields.OriginalFee] = dhaFee == null ? null : new Money(dhaFee.Value);
                tracing.Trace("No fee extensions: original fee follows DHA Fee = {0}", dhaFee?.Value);
                return;
            }

            if (!IsSystemAdministrator(service, context.InitiatingUserId))
            {
                tracing.Trace("Project {0} has fee extensions; blocking DHA Fee change by {1}", target.Id, context.InitiatingUserId);
                throw new InvalidPluginExecutionException(ManualChangeMessage);
            }

            var extensions = FeeExtensionAppliedPlugin.SumActiveExtensions(service, target.Id);
            var newFee = dhaFee?.Value ?? 0m;
            target[ProjectFeeFields.OriginalFee] = new Money(newFee - extensions);
            tracing.Trace("System Administrator correction: DHA Fee {0}, extensions {1}, original fee := {2}", newFee, extensions, newFee - extensions);
        }

        private static bool IsSystemAdministrator(IOrganizationService service, Guid userId)
        {
            var query = new QueryExpression("role") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition("roletemplateid", ConditionOperator.Equal, SystemAdministratorTemplate);
            var link = query.AddLink("systemuserroles", "roleid", "roleid");
            link.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            return service.RetrieveMultiple(query).Entities.Count > 0;
        }
    }
}
