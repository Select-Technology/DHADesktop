using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.ProjectFees.Plugins
{
    /// <summary>
    /// Keeps fwp_originalprojectfee ("Original project fee - as agreed at project start") in step with the DHA Fee
    /// until the first fee extension, then guards the DHA Fee against manual edits.
    ///
    /// REGISTER: synchronous, PRE-OPERATION on msdyn_project
    ///   - Create
    ///   - Update, filtering attributes = isc_projectamount, Pre Image "PreImage" with isc_projectamount
    ///
    ///   * Create: original fee := DHA Fee (when the DHA Fee is supplied).
    ///   * Update of the DHA Fee by a person or flow (Depth 1):
    ///       - no fee extensions yet -> original fee follows the new DHA Fee (corrections before any extension)
    ///       - fee extensions exist  -> blocked; Finance should raise a fee extension instead
    ///   * The update FeeExtensionAppliedPlugin makes (Depth > 1) is left alone.
    /// Setting values on the Target in pre-operation saves them with the same write, so there is no second Update.
    /// </summary>
    public class ProjectFeePlugin : IPlugin
    {
        public const string ManualChangeMessage =
            "This project has fee extensions. Changing the DHA Fee manually is dangerous - if you want to increase the fee, " +
            "please ask Finance to raise a fee extension. If this is a manual correction, ensure you have accounted for the " +
            "fee extensions before saving.";

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

            // Saves and flows that resend an unchanged DHA Fee are not edits
            var before = context.PreEntityImages.Contains("PreImage")
                ? context.PreEntityImages["PreImage"].GetAttributeValue<Money>(ProjectFeeFields.DhaFee)
                : null;
            if (before?.Value == dhaFee?.Value)
            {
                return;
            }

            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(null);

            var query = new QueryExpression(ProjectFeeFields.FeeExtension) { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition(ProjectFeeFields.ExtensionProject, ConditionOperator.Equal, target.Id);
            if (service.RetrieveMultiple(query).Entities.Count > 0)
            {
                tracing.Trace("Project {0} has fee extensions; blocking manual DHA Fee edit", target.Id);
                throw new InvalidPluginExecutionException(ManualChangeMessage);
            }

            target[ProjectFeeFields.OriginalFee] = dhaFee == null ? null : new Money(dhaFee.Value);
            tracing.Trace("Update with no fee extensions: original fee follows DHA Fee = {0}", dhaFee?.Value);
        }
    }
}
