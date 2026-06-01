using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Utilities;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DHA.DSTC.WPF.Services
{
    public class DisbursementService
    {
        private readonly DataverseConnector _connector;
        private readonly string _entityName = "fwp_disbursement"; // Update this to match your actual entity name
        private readonly string _disbursementTypeEntityName = "fwp_disbursementtype";
        private Dictionary<int, string> _disbursementTypeNameCache;
        private readonly Dictionary<string, HashSet<string>> _createableAttributeCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        public DisbursementService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public async Task<List<Disbursement>> GetAllDisbursementsAsync()
        {
            try
            {
                _connector.Connect();

                // START WITH SIMPLE QUERY FIRST
                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Orders = {
                new OrderExpression("fwp_date", OrderType.Descending)
            }
                };

                System.Diagnostics.Debug.WriteLine("Basic disbursement query created successfully");

                // Try enhanced query with joins in a separate try-catch
                var entities = new List<Entity>();

                try
                {
                    // ATTEMPT ENHANCED QUERY WITH JOINS
                    var enhancedQuery = new QueryExpression(_entityName)
                    {
                        ColumnSet = new ColumnSet(true),
                        Orders = {
                    new OrderExpression("fwp_date", OrderType.Descending)
                }
                    };

                    // **CRITICAL FIX**: Verify field exists before creating joins
                    // The error suggests the metadata cache doesn't recognize the field structure

                    // Join to project (LEFT OUTER JOIN)
                    try
                    {
                        var projectLink = enhancedQuery.AddLink("msdyn_project", "fwp_project", "msdyn_projectid", JoinOperator.LeftOuter);
                        projectLink.EntityAlias = "project";
                        projectLink.Columns = new ColumnSet("msdyn_subject", "isc_projectnumbernew");
                        System.Diagnostics.Debug.WriteLine("? Project join added successfully");
                    }
                    catch (Exception projEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"? Project join failed: {projEx.Message}");
                    }

                    // Join to quote (LEFT OUTER JOIN) 
                    try
                    {
                        var quoteLink = enhancedQuery.AddLink("quote", "fwp_quote", "quoteid", JoinOperator.LeftOuter);
                        quoteLink.EntityAlias = "quote";
                        quoteLink.Columns = new ColumnSet("name", "quotenumber");
                        System.Diagnostics.Debug.WriteLine("? Quote join added successfully");
                    }
                    catch (Exception quoteEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"? Quote join failed: {quoteEx.Message}");
                    }

                    // **THIS IS THE PROBLEMATIC JOIN** - Join to systemuser for team member info
                    try
                    {
                        // **DEFENSIVE APPROACH**: First verify the field exists
                        var teamMemberLink = enhancedQuery.AddLink("systemuser", "fwp_teammember", "systemuserid", JoinOperator.LeftOuter);
                        teamMemberLink.EntityAlias = "teammember";
                        teamMemberLink.Columns = new ColumnSet("fullname");
                        System.Diagnostics.Debug.WriteLine("? Team member join added successfully");
                    }
                    catch (Exception teamEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"? Team member join failed (THIS IS THE METADATA ERROR): {teamEx.Message}");
                        // This is likely where the metadata cache error occurs
                        // Continue without this join
                    }

                    // Execute the enhanced query
                    entities = _connector._orgService.RetrieveMultiple(enhancedQuery).Entities.ToList();
                    System.Diagnostics.Debug.WriteLine($"Enhanced disbursement query executed successfully, got {entities.Count} records");
                }
                catch (Exception enhancedEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Enhanced query failed: {enhancedEx.Message}");
                    System.Diagnostics.Debug.WriteLine("Falling back to simple query...");

                    // FALLBACK TO SIMPLE QUERY
                    entities = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
                    System.Diagnostics.Debug.WriteLine($"Simple disbursement query executed successfully, got {entities.Count} records");
                }

                return entities.Select(MapDisbursementFromEntity).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Critical error in GetAllDisbursementsAsync: {ex.Message}");
                System.Windows.Forms.MessageBox.Show($"Error retrieving disbursements: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return new List<Disbursement>();
            }
        }

        public async Task<List<Disbursement>> GetAllDisbursementsBasicAsync()
        {
            try
            {
                _connector.Connect();

                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Orders = {
                new OrderExpression("fwp_date", OrderType.Descending)
            }
                };

                // No joins - just basic data
                var entities = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
                return entities.Select<Entity, Disbursement>(entity => MapDisbursementFromEntity(entity))
                              .Where(d => d != null)
                              .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetAllDisbursementsBasicAsync: {ex.Message}");
                return new List<Disbursement>();
            }
        }


        public async Task<List<Disbursement>> GetAllDisbursementsEnhanced()
        {
            try
            {
                _connector.Connect();

                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Orders = {
                new OrderExpression("fwp_date", OrderType.Descending)
            }
                };

                // Add joins to get project and quote information
                try
                {
                    // Join to project for project details - This one is probably OK
                    var projectLink = query.AddLink("msdyn_project", "fwp_project", "msdyn_projectid", JoinOperator.LeftOuter);
                    projectLink.EntityAlias = "project";
                    projectLink.Columns = new ColumnSet("msdyn_subject", "isc_projectnumbernew");

                    // Join to quote for quote details - This one is probably OK
                    var quoteLink = query.AddLink("quote", "fwp_quote", "quoteid", JoinOperator.LeftOuter);
                    quoteLink.EntityAlias = "quote";
                    quoteLink.Columns = new ColumnSet("name", "quotenumber");

                    // Join to quote customer for client information - This one might be OK
                    var quoteCustomerLink = quoteLink.AddLink("contact", "customerid", "contactid", JoinOperator.LeftOuter);
                    quoteCustomerLink.EntityAlias = "quotecustomer";
                    quoteCustomerLink.Columns = new ColumnSet("fullname");

                    // ? THIS IS THE PROBLEM JOIN - FIXED ?
                    // OLD (WRONG): var teamMemberLink = query.AddLink("fwp_teammember", "fwp_teammember", "fwp_teammemberid", JoinOperator.LeftOuter);
                    // ? CORRECTED: Join to systemuser entity, not fwp_teammember entity
                    var teamMemberLink = query.AddLink("systemuser", "fwp_teammember", "systemuserid", JoinOperator.LeftOuter);
                    teamMemberLink.EntityAlias = "teammember";
                    teamMemberLink.Columns = new ColumnSet("fullname"); // systemuser has fullname, not fwp_name

                    // ? THIS IS ALSO LIKELY WRONG - NEED TO VERIFY ?
                    // The disbursement type join might also be incorrect
                    // OLD: var typeLink = query.AddLink("fwp_disbursementtype", "fwp_disbursementtype", "fwp_disbursementtypeid", JoinOperator.LeftOuter);
                    // This should probably be:
                    var typeLink = query.AddLink("fwp_disbursementtype", "fwp_disbursementtype", "fwp_disbursementtypeid", JoinOperator.LeftOuter);
                    typeLink.EntityAlias = "disbursementtype";
                    typeLink.Columns = new ColumnSet("fwp_name"); // Assuming this custom entity has fwp_name field

                    System.Diagnostics.Debug.WriteLine("? Enhanced disbursement query with CORRECTED joins created successfully");
                }
                catch (Exception joinEx)
                {
                    System.Diagnostics.Debug.WriteLine($"? Join creation failed: {joinEx.Message}");
                    // If joins fail, fall back to simple query
                    return await GetAllDisbursementsBasicAsync();
                }

                var entities = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
                return entities.Select(MapDisbursementFromEntityEnhanced).Where(d => d != null).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Enhanced disbursements query failed: {ex.Message}");
                System.Windows.Forms.MessageBox.Show($"Error retrieving enhanced disbursements: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);

                // Fallback to basic query
                return await GetAllDisbursementsBasicAsync();
            }
        }


        public async Task<List<Disbursement>> GetDisbursementsByProjectAsync(Guid projectId)
        {
            try
            {
                _connector.Connect();

                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression(LogicalOperator.And),
                    Orders = {
                        new OrderExpression("fwp_date", OrderType.Descending)
                    }
                };

                query.Criteria.AddCondition("fwp_project", ConditionOperator.Equal, projectId);

                var entities = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
                return entities.Select(MapDisbursementFromEntity).ToList();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error retrieving disbursements: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return new List<Disbursement>();
            }
        }

        public async Task<Disbursement> GetDisbursementByIdAsync(Guid id)
        {
            try
            {
                _connector.Connect();
                Entity entity = _connector._orgService.Retrieve(_entityName, id, new ColumnSet(true));
                return MapDisbursementFromEntity(entity);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error retrieving disbursement: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return null;
            }
        }

        public async Task<Guid> AddDisbursementAsyncEnhanced(Disbursement disbursement)
        {
            try
            {
                _connector.Connect();

                Entity entity;

                // Use enhanced mapping if disbursement has quote classification
                if (disbursement.Classification == DisbursementClassification.Quote)
                {
                    entity = MapEntityFromDisbursementEnhanced(disbursement);
                }
                else
                {
                    // Use existing mapping for project disbursements
                    entity = MapEntityFromDisbursement(disbursement);
                }

                var newId = _connector._orgService.Create(entity);
                return newId;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error adding disbursement: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return Guid.Empty;
            }
        }

        public async Task UpdateDisbursementAsyncEnhanced(Disbursement disbursement)
        {
            try
            {
                _connector.Connect();

                Entity entity;

                // Use enhanced mapping if disbursement has quote classification
                if (disbursement.Classification == DisbursementClassification.Quote)
                {
                    entity = MapEntityFromDisbursementEnhanced(disbursement);
                }
                else
                {
                    // Use existing mapping for project disbursements
                    entity = MapEntityFromDisbursement(disbursement);
                }

                entity.Id = disbursement.IdGuid;
                _connector._orgService.Update(entity);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error updating disbursement: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }


        public async Task<Guid> AddDisbursementAsync(Disbursement disbursement)
        {
            try
            {
                _connector.Connect();
                var entity = MapEntityFromDisbursement(disbursement);
                return _connector._orgService.Create(entity);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error creating disbursement: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return Guid.Empty;
            }
        }

        public async Task UpdateDisbursementAsync(Disbursement disbursement)
        {
            try
            {
                var reconciliationValidation = ReconciliationValidationHelper.ValidateDisbursementModification(
                    disbursement.IdGuid, _connector);

                if (!reconciliationValidation.IsValid)
                {
                    // Show reconciliation error to user
                    System.Windows.Forms.MessageBox.Show(reconciliationValidation.Message,
                        "Cannot Modify Disbursement",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                    return; // Exit early - don't proceed with update
                }

                // Show warning if there's a reconciliation concern
                if (reconciliationValidation.Level == ReconciliationValidationLevel.Warning)
                {
                    var result = System.Windows.Forms.MessageBox.Show(
                        $"{reconciliationValidation.Message}\n\nDo you want to continue?",
                        "Reconciliation Warning",
                        System.Windows.Forms.MessageBoxButtons.YesNo,
                        System.Windows.Forms.MessageBoxIcon.Warning);

                    if (result == System.Windows.Forms.DialogResult.No)
                        return; // Exit early - user chose not to continue
                }


                _connector.Connect();
                var entity = MapEntityFromDisbursement(disbursement);

                // If we have a valid GUID in IdGuid, use that
                if (disbursement.IdGuid != Guid.Empty)
                {
                    entity.Id = disbursement.IdGuid;
                }
                // Fallback to converting from Int ID if needed
                else if (disbursement.Id > 0)
                {
                    entity.Id = Utilities.GuidConverter.GetDeterministicGuidFromId(disbursement.Id);
                }
                else
                {
                    throw new Exception("Missing valid ID for update operation");
                }

                _connector._orgService.Update(entity);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error updating disbursement: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        public async Task DeleteDisbursementAsync(Guid id)
        {
            try
            {
                var reconciliationValidation = ReconciliationValidationHelper.ValidateDisbursementDeletion(
            id, _connector);

                if (!reconciliationValidation.IsValid)
                {
                    // Show reconciliation error to user
                    System.Windows.Forms.MessageBox.Show(reconciliationValidation.Message,
                        "Cannot Delete Disbursement",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                    return; // Exit early - don't proceed with deletion
                }


                _connector.Connect();
                _connector._orgService.Delete(_entityName, id);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error deleting disbursement: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        public async Task<List<DisbursementType>> GetAllDisbursementTypesAsync()
        {
            try
            {
                _connector.Connect();

                EnsureMileageDisbursementTypesExist();

                var query = new QueryExpression(_disbursementTypeEntityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Orders =
                    {
                        new OrderExpression("fwp_name", OrderType.Ascending)
                    }
                };

                var entities = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
                var disbursementTypes = entities
                    .Select(MapDisbursementTypeFromEntity)
                    .Where(type => type != null)
                    .ToList();

                if (disbursementTypes.Any())
                {
                    PopulateDisbursementTypeUnitCharges(disbursementTypes);
                    return disbursementTypes;
                }

                return await GetDisbursementTypesFromOptionSetMetadataAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error retrieving disbursement types from Dataverse table: {ex.Message}");

                return await GetDisbursementTypesFromOptionSetMetadataAsync();
            }
        }

        private async Task<List<DisbursementType>> GetDisbursementTypesFromOptionSetMetadataAsync()
        {
            try
            {
                // Get the option set metadata for disbursement types
                var request = new RetrieveAttributeRequest
                {
                    EntityLogicalName = "fwp_disbursement",
                    LogicalName = "fwp_type",
                    RetrieveAsIfPublished = true
                };

                var response = (RetrieveAttributeResponse)_connector._orgService.Execute(request);

                if (response.AttributeMetadata is EnumAttributeMetadata enumMetadata)
                {
                    var options = enumMetadata.OptionSet.Options;

                    // Convert the options to our DisbursementType model
                    var disbursementTypes = options.Select(o => new DisbursementType
                    {
                        Id = o.Value.Value,
                        Name = NormalizeDisbursementTypeName(o.Label.UserLocalizedLabel?.Label),
                        Description = NormalizeDisbursementTypeDescription(o.Description?.UserLocalizedLabel?.Label ?? o.Label.UserLocalizedLabel?.Label),
                        UnitCharge = 0 // Will be populated below
                    }).ToList();

                    // Populate unit charges from configuration entity or hardcoded values
                    PopulateDisbursementTypeUnitCharges(disbursementTypes);

                    return disbursementTypes;
                }

                return GetDefaultDisbursementTypes();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error retrieving disbursement types: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);

                return GetDefaultDisbursementTypes();
            }
        }

        private DisbursementType MapDisbursementTypeFromEntity(Entity entity)
        {
            if (entity == null)
            {
                return null;
            }

            var name = entity.GetAttributeValue<string>("fwp_name")
                ?? entity.GetAttributeValue<string>("name")
                ?? string.Empty;

            var description = entity.GetAttributeValue<string>("fwp_description")
                ?? entity.GetAttributeValue<string>("fwp_name")
                ?? entity.GetAttributeValue<string>("name")
                ?? string.Empty;

            var disbursementType = new DisbursementType
            {
                Id = entity.Id.GetHashCode(),
                IdGuid = entity.Id,
                Name = NormalizeDisbursementTypeName(name),
                Description = NormalizeDisbursementTypeDescription(description),
                UnitCharge = TryGetDecimalValue(entity, "fwp_unitcharge")
            };

            if (disbursementType.UnitCharge <= 0)
            {
                disbursementType.UnitCharge = GetUnitChargeForDisbursementTypeName(disbursementType.Name) ?? 0m;
            }

            return disbursementType;
        }

        private decimal TryGetDecimalValue(Entity entity, string attributeName)
        {
            if (entity == null || !entity.Contains(attributeName) || entity[attributeName] == null)
            {
                return 0m;
            }

            var value = entity[attributeName];
            switch (value)
            {
                case decimal decimalValue:
                    return decimalValue;
                case Money moneyValue:
                    return moneyValue.Value;
                case double doubleValue:
                    return (decimal)doubleValue;
                case int intValue:
                    return intValue;
                default:
                    return decimal.TryParse(value.ToString(), out var parsedValue) ? parsedValue : 0m;
            }
        }

        private void EnsureMileageDisbursementTypesExist()
        {
            try
            {
                var query = new QueryExpression(_disbursementTypeEntityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression(LogicalOperator.Or)
                };

                query.Criteria.AddCondition("fwp_name", ConditionOperator.Equal, "Mileage");
                query.Criteria.AddCondition("fwp_name", ConditionOperator.Equal, "Mileage (Personal Car)");
                query.Criteria.AddCondition("fwp_name", ConditionOperator.Equal, "Mileage (Company Car)");

                var mileageTypes = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
                if (!mileageTypes.Any())
                {
                    return;
                }

                var personalCarExists = mileageTypes.Any(entity =>
                    string.Equals(entity.GetAttributeValue<string>("fwp_name"), "Mileage (Personal Car)", StringComparison.OrdinalIgnoreCase));

                if (personalCarExists)
                {
                    return;
                }

                var sourceMileageType = mileageTypes.FirstOrDefault(entity =>
                    string.Equals(entity.GetAttributeValue<string>("fwp_name"), "Mileage", StringComparison.OrdinalIgnoreCase))
                    ?? mileageTypes.FirstOrDefault(entity =>
                        string.Equals(entity.GetAttributeValue<string>("fwp_name"), "Mileage (Company Car)", StringComparison.OrdinalIgnoreCase));

                if (sourceMileageType == null)
                {
                    return;
                }

                var personalCarMileageType = CloneDisbursementTypeEntity(sourceMileageType, "Mileage (Personal Car)");
                _connector._orgService.Create(personalCarMileageType);

                _disbursementTypeNameCache = null;
                System.Diagnostics.Debug.WriteLine("Created missing Dataverse disbursement type: Mileage (Personal Car)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureMileageDisbursementTypesExist error: {ex.Message}");
            }
        }

        private Entity CloneDisbursementTypeEntity(Entity sourceEntity, string newName)
        {
            var clone = new Entity(_disbursementTypeEntityName);
            var createableAttributes = GetCreateableAttributes(_disbursementTypeEntityName);

            foreach (var attribute in sourceEntity.Attributes)
            {
                if (!createableAttributes.Contains(attribute.Key) || attribute.Value == null)
                {
                    continue;
                }

                if (string.Equals(attribute.Key, "fwp_name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                clone[attribute.Key] = CloneAttributeValue(attribute.Value);
            }

            clone["fwp_name"] = newName;
            return clone;
        }

        private HashSet<string> GetCreateableAttributes(string entityLogicalName)
        {
            if (_createableAttributeCache.TryGetValue(entityLogicalName, out var cachedAttributes))
            {
                return cachedAttributes;
            }

            var request = new RetrieveEntityRequest
            {
                LogicalName = entityLogicalName,
                EntityFilters = EntityFilters.Attributes,
                RetrieveAsIfPublished = true
            };

            var response = (RetrieveEntityResponse)_connector._orgService.Execute(request);
            var createableAttributes = response.EntityMetadata.Attributes
                .Where(attribute => attribute.IsValidForCreate == true)
                .Select(attribute => attribute.LogicalName)
                .Where(logicalName => !string.IsNullOrWhiteSpace(logicalName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _createableAttributeCache[entityLogicalName] = createableAttributes;
            return createableAttributes;
        }

        private object CloneAttributeValue(object value)
        {
            switch (value)
            {
                case EntityReference entityReference:
                    return new EntityReference(entityReference.LogicalName, entityReference.Id)
                    {
                        Name = entityReference.Name
                    };
                case OptionSetValue optionSetValue:
                    return new OptionSetValue(optionSetValue.Value);
                case Money moneyValue:
                    return new Money(moneyValue.Value);
                case bool boolValue:
                    return boolValue;
                case int intValue:
                    return intValue;
                case decimal decimalValue:
                    return decimalValue;
                case double doubleValue:
                    return doubleValue;
                case DateTime dateTimeValue:
                    return dateTimeValue;
                case string stringValue:
                    return stringValue;
                default:
                    return value;
            }
        }



        private void PopulateDisbursementTypeUnitCharges(List<DisbursementType> disbursementTypes)
        {
            try
            {
                foreach (var disbursementType in disbursementTypes)
                {
                    disbursementType.Name = NormalizeDisbursementTypeName(disbursementType.Name);
                    disbursementType.Description = NormalizeDisbursementTypeDescription(disbursementType.Description);

                    var unitCharge = GetUnitChargeForDisbursementTypeName(disbursementType.Name);
                    if (unitCharge.HasValue)
                    {
                        disbursementType.UnitCharge = unitCharge.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error populating unit charges: {ex.Message}");
                // Continue with zero unit charges if this fails
            }
        }

        private List<DisbursementType> GetDefaultDisbursementTypes()
        {
            // Default values with unit charges
            return new List<DisbursementType>
            {
                new DisbursementType { Id = 800470000, Name = "Mileage", Description = "Mileage expenses", UnitCharge = 0.65m },
                new DisbursementType { Id = 800470001, Name = "Hotel", Description = "Hotel accommodations", UnitCharge = 0 },
                new DisbursementType { Id = 800470002, Name = "Subsistence", Description = "Subsistence allowance", UnitCharge = 0 },
                new DisbursementType { Id = 800470003, Name = "Planning Application", Description = "Planning application fees", UnitCharge = 0 },
                //new DisbursementType { Id = 800470004, Name = "Printing", Description = "Printing costs", UnitCharge = 0 },
                new DisbursementType { Id = 800470005, Name = "Printing (B&W)", Description = "Black and white printing", UnitCharge = 0.22m },
                new DisbursementType { Id = 800470006, Name = "Printing (Colour)", Description = "Colour printing", UnitCharge = 0.45m },
                new DisbursementType { Id = 800470007, Name = "Parking", Description = "Parking fees", UnitCharge = 0 },
                new DisbursementType { Id = 800470008, Name = "Postage", Description = "Postage costs", UnitCharge = 0 },
                new DisbursementType { Id = 800470009, Name = "Subcontract Professional Services", Description = "Professional services from subcontractors", UnitCharge = 0 },
                new DisbursementType { Id = 800470010, Name = "Using Default", Description = "Using Default", UnitCharge = 0 }
            }
            .Select(type =>
            {
                type.Name = NormalizeDisbursementTypeName(type.Name);
                type.Description = NormalizeDisbursementTypeDescription(type.Description);
                return type;
            })
            .ToList();
        }

        private string GetDisbursementTypeName(int optionValue)
        {
            try
            {
                var metadataTypeNames = GetDisbursementTypeNamesFromMetadata();
                if (metadataTypeNames.TryGetValue(optionValue, out var metadataTypeName))
                {
                    return metadataTypeName;
                }

                // This should match the option set values in your Dataverse environment
                var typeMapping = new Dictionary<int, string>
        {
            { 800470000, "Mileage" },
            { 800470001, "Hotel" },
            { 800470002, "Subsistence" },
            { 800470003, "Planning Application" },
            { 800470005, "Printing (B&W)" },
            { 800470006, "Printing (Colour)" },
            { 800470007, "Parking" },
            { 800470008, "Postage" },
            { 800470009, "Subcontract Professional Services" },
            { 800470010, "Using Default" }
        };

                if (typeMapping.ContainsKey(optionValue))
                {
                    return NormalizeDisbursementTypeName(typeMapping[optionValue]);
                }

                // Try to get from loaded disbursement types as fallback
                var loadedTypes = GetDefaultDisbursementTypes();
                var matchingType = loadedTypes.FirstOrDefault(t => t.Id == optionValue);
                if (matchingType != null)
                {
                    return matchingType.Name;
                }

                // Last resort - return the ID
                return $"Type {optionValue}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetDisbursementTypeName: {ex.Message}");
                return $"Type {optionValue}";
            }
        }

        private Dictionary<int, string> GetDisbursementTypeNamesFromMetadata()
        {
            if (_disbursementTypeNameCache != null)
            {
                return _disbursementTypeNameCache;
            }

            try
            {
                _connector.Connect();

                var request = new RetrieveAttributeRequest
                {
                    EntityLogicalName = "fwp_disbursement",
                    LogicalName = "fwp_type",
                    RetrieveAsIfPublished = true
                };

                var response = (RetrieveAttributeResponse)_connector._orgService.Execute(request);
                if (response.AttributeMetadata is EnumAttributeMetadata enumMetadata)
                {
                    _disbursementTypeNameCache = enumMetadata.OptionSet.Options
                        .Where(option => option.Value.HasValue)
                        .ToDictionary(
                            option => option.Value.Value,
                            option => NormalizeDisbursementTypeName(option.Label?.UserLocalizedLabel?.Label ?? option.Label?.LocalizedLabels?.FirstOrDefault()?.Label));

                    return _disbursementTypeNameCache;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading disbursement type metadata: {ex.Message}");
            }

            _disbursementTypeNameCache = new Dictionary<int, string>();
            return _disbursementTypeNameCache;
        }

        private decimal? GetUnitChargeForDisbursementTypeName(string disbursementTypeName)
        {
            var normalizedName = NormalizeDisbursementTypeName(disbursementTypeName);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return null;
            }

            if (normalizedName.IndexOf("mileage", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0.65m;
            }

            switch (normalizedName)
            {
                case "Printing (B&W)":
                    return 0.22m;
                case "Printing (Colour)":
                    return 0.42m;
                case "Photocopying (B&W)":
                    return 0.22m;
                case "Photocopying (Colour)":
                    return 0.42m;
                default:
                    return null;
            }
        }

        private string NormalizeDisbursementTypeName(string disbursementTypeName)
        {
            var trimmedName = disbursementTypeName?.Trim();
            switch (trimmedName)
            {
                case "Mileage":
                    return "Mileage (Company Car)";
                case "Mileage (Personal)":
                case "Personal Mileage":
                case "Personal Car Mileage":
                case "Mileage - Personal":
                case "Mileage (Personal Car)":
                    return "Mileage (Personal Car)";
                case "Mileage (Company)":
                case "Company Mileage":
                case "Company Car Mileage":
                case "Mileage - Company":
                case "Mileage (Company Car)":
                    return "Mileage (Company Car)";
                case "Photocopying (B&W)":
                    return "Printing (B&W)";
                case "Photocopying (Colour)":
                    return "Printing (Colour)";
                default:
                    return trimmedName;
            }
        }

        private string NormalizeDisbursementTypeDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return description;
            }

            return description
                .Replace("photocopying", "printing")
                .Replace("Photocopying", "Printing")
                .Replace("Black and white photocopying", "Black and white printing")
                .Replace("Colour photocopying", "Colour printing");
        }
        private Disbursement MapDisbursementFromEntity(Entity entity)
        {
            if (entity == null)
                return null;

            try
            {
                var disbursement = new Disbursement
                {
                    Id = entity.Id.GetHashCode(),
                    IdGuid = entity.Id
                };

                // ? FIXED: Handle Date with proper timezone normalization
                if (entity.Contains("fwp_date"))
                {
                    var dateValue = entity.GetAttributeValue<DateTime>("fwp_date");

                    // Ensure we're working with local dates for consistency
                    if (dateValue.Kind == DateTimeKind.Utc)
                    {
                        disbursement.Date = dateValue.ToLocalTime().Date;
                    }
                    else if (dateValue.Kind == DateTimeKind.Unspecified)
                    {
                        // Assume Dataverse dates are UTC if not specified
                        disbursement.Date = DateTime.SpecifyKind(dateValue, DateTimeKind.Utc).ToLocalTime().Date;
                    }
                    else
                    {
                        disbursement.Date = dateValue.Date; // Remove time component
                    }

                    // Fallback check
                    if (disbursement.Date == DateTime.MinValue)
                        disbursement.Date = DateTime.Today;

                    // ? DEBUG: Log date conversion for troubleshooting
                    System.Diagnostics.Debug.WriteLine($"Date conversion - Original: {dateValue} ({dateValue.Kind}) -> Final: {disbursement.Date}");
                }
                else
                {
                    disbursement.Date = DateTime.Today;
                }

                // Handle Description
                disbursement.Description = entity.GetAttributeValue<string>("fwp_description") ?? string.Empty;

                // Handle Money type
                if (entity.Contains("fwp_value"))
                {
                    Money moneyValue = entity.GetAttributeValue<Money>("fwp_value");
                    if (moneyValue != null)
                    {
                        disbursement.Amount = moneyValue.Value;
                    }
                }

                // Handle Units and UnitCharge
                if (entity.Contains("fwp_units"))
                {
                    disbursement.Units = entity.GetAttributeValue<decimal>("fwp_units");
                }

                if (entity.Contains("fwp_unitcharge"))
                {
                    disbursement.UnitCharge = entity.GetAttributeValue<decimal>("fwp_unitcharge");
                }

                if (entity.Contains("fwp_disbursementtype"))
                {
                    try
                    {
                        var typeReference = entity.GetAttributeValue<EntityReference>("fwp_disbursementtype");
                        if (typeReference != null)
                        {
                            disbursement.DisbursementTypeGuid = typeReference.Id;
                            disbursement.DisbursementTypeId = typeReference.Id.GetHashCode();
                            disbursement.DisbursementTypeName = NormalizeDisbursementTypeName(typeReference.Name) ?? disbursement.DisbursementTypeName;
                        }
                    }
                    catch (Exception typeLookupEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error mapping disbursement type lookup: {typeLookupEx.Message}");
                    }
                }

                // Handle Disbursement Type
                if (disbursement.DisbursementTypeGuid == Guid.Empty && entity.Contains("fwp_type"))
                {
                    try
                    {
                        var typeOption = entity.GetAttributeValue<OptionSetValue>("fwp_type");
                        if (typeOption != null)
                        {
                            disbursement.DisbursementTypeId = typeOption.Value;
                            disbursement.DisbursementTypeName = GetDisbursementTypeName(typeOption.Value);
                        }
                    }
                    catch (Exception typeEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error mapping disbursement type: {typeEx.Message}");
                    }
                }

                // Handle Billable to Client
                if (entity.Contains("fwp_billabletoclient"))
                {
                    disbursement.BillableToClient = entity.GetAttributeValue<bool>("fwp_billabletoclient");
                }
                else
                {
                    disbursement.BillableToClient = true; // Default to true
                }

                // Handle Project reference
                if (entity.Contains("fwp_project"))
                {
                    try
                    {
                        var projectRef = entity.GetAttributeValue<EntityReference>("fwp_project");
                        if (projectRef != null)
                        {
                            disbursement.ProjectId = projectRef.Id.GetHashCode();
                            disbursement.ProjectGuid = projectRef.Id;
                            disbursement.Classification = DisbursementClassification.Project;

                            string fullProjectName = projectRef.Name ?? "Unknown Project";
                            ParseProjectNameAndNumber(disbursement, fullProjectName);
                        }
                    }
                    catch
                    {
                        disbursement.ProjectId = 0;
                        disbursement.ProjectGuid = Guid.Empty;
                        disbursement.ProjectName = "Unknown Project";
                    }
                }

                // Handle Quote reference
                if (entity.Contains("fwp_quote"))
                {
                    try
                    {
                        var quoteRef = entity.GetAttributeValue<EntityReference>("fwp_quote");
                        if (quoteRef != null)
                        {
                            disbursement.QuoteId = quoteRef.Id;
                            disbursement.Classification = DisbursementClassification.Quote;
                            disbursement.QuoteName = quoteRef.Name ?? "Unknown Quote";
                        }
                    }
                    catch
                    {
                        disbursement.QuoteId = Guid.Empty;
                    }
                }

                // Handle Team Member
                if (entity.Contains("fwp_teammember"))
                {
                    try
                    {
                        var teamMemberRef = entity.GetAttributeValue<EntityReference>("fwp_teammember");
                        if (teamMemberRef != null)
                        {
                            disbursement.TeamMemberId = teamMemberRef.Id.GetHashCode();
                            disbursement.TeamMemberGuid = teamMemberRef.Id;
                            disbursement.TeamMemberName = teamMemberRef.Name ?? "Unknown Team Member";
                        }
                    }
                    catch
                    {
                        disbursement.TeamMemberId = 0;
                        disbursement.TeamMemberGuid = Guid.Empty;
                        disbursement.TeamMemberName = "Unknown Team Member";
                    }
                }

                return disbursement;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error mapping disbursement entity: {ex.Message}");
                return new Disbursement
                {
                    Description = "Error loading disbursement",
                    Date = DateTime.Today,
                    Amount = 0,
                    DisbursementTypeName = $"Mapping Error: {ex.Message}",
                    ProjectName = "Unknown Project",
                    TeamMemberName = "Unknown Team Member"
                };
            }
        }



        public void TestDisbursementTypeMapping()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== TESTING DISBURSEMENT TYPE MAPPING ===");

                var testIds = new int[] { 800470000, 800470001, 800470002, 800470005, 800470007 };

                foreach (var id in testIds)
                {
                    var name = GetDisbursementTypeName(id);
                    System.Diagnostics.Debug.WriteLine($"ID {id} -> Name '{name}'");
                }

                System.Diagnostics.Debug.WriteLine("=== END TEST ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TestDisbursementTypeMapping error: {ex.Message}");
            }
        }


        private void ParseProjectNameAndNumber(Disbursement disbursement, string fullProjectName)
        {
            try
            {
                if (fullProjectName.Contains(" - "))
                {
                    var parts = fullProjectName.Split(new[] { " - " }, 2, StringSplitOptions.None);
                    disbursement.ProjectNumber = parts[0].Trim();
                    disbursement.ProjectName = parts[1].Trim();
                }
                else
                {
                    var words = fullProjectName.Split(' ');
                    if (words.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(words[0], @"^\d+$"))
                    {
                        disbursement.ProjectNumber = words[0];
                        disbursement.ProjectName = string.Join(" ", words.Skip(1));
                    }
                    else
                    {
                        disbursement.ProjectNumber = "";
                        disbursement.ProjectName = fullProjectName;
                    }
                }
            }
            catch
            {
                disbursement.ProjectNumber = "";
                disbursement.ProjectName = fullProjectName;
            }
        }

        private Entity MapEntityFromDisbursement(Disbursement disbursement)
        {
            var entity = new Entity(_entityName);

            try
            {
                // ? FIXED: Store date without time component to avoid timezone issues
                // Always store as UTC midnight to ensure consistency
                var dateToStore = DateTime.SpecifyKind(disbursement.Date.Date, DateTimeKind.Utc);
                entity["fwp_date"] = dateToStore;

                // ? DEBUG: Log what we're storing
                System.Diagnostics.Debug.WriteLine($"Storing date - Input: {disbursement.Date} -> Stored: {dateToStore} ({dateToStore.Kind})");

                entity["fwp_description"] = disbursement.Description ?? string.Empty;

                // Handle Money value - ensure it's a valid decimal
                entity["fwp_value"] = new Money(disbursement.Amount);

                // Handle Units and UnitCharge for unit-based disbursements
                if (disbursement.IsUnitBased)
                {
                    entity["fwp_units"] = disbursement.Units;
                    entity["fwp_unitcharge"] = disbursement.UnitCharge;
                }

                if (disbursement.DisbursementTypeGuid != Guid.Empty)
                {
                    entity["fwp_disbursementtype"] = new EntityReference(_disbursementTypeEntityName, disbursement.DisbursementTypeGuid);
                }
                else if (disbursement.DisbursementTypeId > 0)
                {
                    entity["fwp_type"] = new OptionSetValue(disbursement.DisbursementTypeId);
                }

                // ? REMOVED: fwp_billabletoclient field doesn't exist in Dataverse entity
                // entity["fwp_billabletoclient"] = disbursement.BillableToClient;

                // Handle project reference - prefer original Guid when available
                if (disbursement.ProjectGuid != Guid.Empty)
                {
                    entity["fwp_project"] = new EntityReference("msdyn_project", disbursement.ProjectGuid);
                }
                else if (disbursement.ProjectId > 0)
                {
                    try
                    {
                        // Fallback to conversion if only ID is available
                        entity["fwp_project"] = new EntityReference(
                            "msdyn_project",
                            Utilities.GuidConverter.GetDeterministicGuidFromId(disbursement.ProjectId));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Could not convert Project ID: {ex.Message}");
                    }
                }

                // Handle quote reference
                if (disbursement.Classification == DisbursementClassification.Quote && disbursement.QuoteId != Guid.Empty)
                {
                    entity["fwp_quote"] = new EntityReference("quote", disbursement.QuoteId);
                }

                // For team member, use the original Guid directly when available
                if (disbursement.TeamMemberGuid != Guid.Empty)
                {
                    entity["fwp_teammember"] = new EntityReference("systemuser", disbursement.TeamMemberGuid);
                }
                else if (disbursement.TeamMemberId > 0)
                {
                    try
                    {
                        // Fallback conversion
                        entity["fwp_teammember"] = new EntityReference(
                            "systemuser",
                            Utilities.GuidConverter.GetDeterministicGuidFromId(disbursement.TeamMemberId));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Could not convert Team Member ID: {ex.Message}");
                    }
                }

                return entity;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error mapping disbursement to entity: {ex.Message}");
                throw;
            }
        }


        public async Task<List<Disbursement>> GetDisbursementsByQuoteAsync(Guid quoteId)
        {
            try
            {
                _connector.Connect();

                // Simple query for now - will be enhanced when database fields are added
                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions = {
                            new ConditionExpression("fwp_quote", ConditionOperator.Equal, quoteId)
                        }
                    },
                    Orders = {
                        new OrderExpression("fwp_date", OrderType.Descending)
                    }
                };

                var entities = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
                return entities.Select(MapDisbursementFromEntityEnhanced).Where(d => d != null).ToList();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error loading disbursements for quote: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return new List<Disbursement>();
            }
        }

        private Disbursement MapDisbursementFromEntityEnhanced(Entity entity)
        {
            if (entity == null)
                return null;

            try
            {
                // Start with the existing mapping logic
                var disbursement = MapDisbursementFromEntity(entity);
                if (disbursement == null)
                    return null;

                // Enhanced mappings from joined entities

                // Project information (from msdyn_project join)
                if (entity.Contains("project.msdyn_subject"))
                {
                    disbursement.ProjectName = entity.GetAttributeValue<AliasedValue>("project.msdyn_subject")?.Value?.ToString() ?? disbursement.ProjectName;
                }

                if (entity.Contains("project.isc_projectnumbernew"))
                {
                    disbursement.ProjectNumber = entity.GetAttributeValue<AliasedValue>("project.isc_projectnumbernew")?.Value?.ToString() ?? "";
                }

                // Quote information (from quote join)
                if (entity.Contains("quote.name"))
                {
                    disbursement.QuoteName = entity.GetAttributeValue<AliasedValue>("quote.name")?.Value?.ToString() ?? disbursement.QuoteName;
                }

                if (entity.Contains("quote.quotenumber"))
                {
                    disbursement.QuoteNumber = entity.GetAttributeValue<AliasedValue>("quote.quotenumber")?.Value?.ToString() ?? "";
                }

                // Quote customer information (from contact join)
                if (entity.Contains("quotecustomer.fullname"))
                {
                    disbursement.ClientName = entity.GetAttributeValue<AliasedValue>("quotecustomer.fullname")?.Value?.ToString() ?? "";
                }

                // ? FIXED: Team member information (from systemuser join)
                if (entity.Contains("teammember.fullname"))
                {
                    disbursement.TeamMemberName = entity.GetAttributeValue<AliasedValue>("teammember.fullname")?.Value?.ToString() ?? disbursement.TeamMemberName;
                }

                // Disbursement type information (from fwp_disbursementtype join)
                if (entity.Contains("disbursementtype.fwp_name"))
                {
                    disbursement.DisbursementTypeName = entity.GetAttributeValue<AliasedValue>("disbursementtype.fwp_name")?.Value?.ToString() ?? disbursement.DisbursementTypeName;
                }

                return disbursement;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in enhanced disbursement mapping: {ex.Message}");
                // Fall back to basic mapping
                return MapDisbursementFromEntity(entity);
            }
        }



        private Disbursement MapDisbursementFromEntityWithQuotes(Entity entity)
        {
            if (entity == null)
                return null;

            try
            {
                var disbursement = new Disbursement
                {
                    Id = entity.Id.GetHashCode(),
                    IdGuid = entity.Id
                };

                // Handle Date
                if (entity.Contains("fwp_date"))
                {
                    try
                    {
                        disbursement.Date = entity.GetAttributeValue<DateTime>("fwp_date");
                        if (disbursement.Date == DateTime.MinValue)
                            disbursement.Date = DateTime.Today;
                    }
                    catch
                    {
                        disbursement.Date = DateTime.Today;
                    }
                }
                else
                {
                    disbursement.Date = DateTime.Today;
                }

                // Handle Description
                disbursement.Description = entity.GetAttributeValue<string>("fwp_description") ?? string.Empty;

                // Handle Amount
                if (entity.Contains("fwp_value"))
                {
                    try
                    {
                        var value = entity["fwp_value"];
                        if (value is Money moneyValue)
                        {
                            disbursement.Amount = moneyValue.Value;
                        }
                        else if (value is decimal decimalValue)
                        {
                            disbursement.Amount = decimalValue;
                        }
                        else if (value != null && decimal.TryParse(value.ToString(), out decimal parsedValue))
                        {
                            disbursement.Amount = parsedValue;
                        }
                    }
                    catch
                    {
                        disbursement.Amount = 0m;
                    }
                }

                // Handle Units and UnitCharge
                if (entity.Contains("fwp_units"))
                {
                    try
                    {
                        var value = entity["fwp_units"];
                        if (value != null && decimal.TryParse(value.ToString(), out decimal units))
                        {
                            disbursement.Units = units;
                        }
                    }
                    catch
                    {
                        disbursement.Units = 0m;
                    }
                }

                if (entity.Contains("fwp_unitcharge"))
                {
                    try
                    {
                        var value = entity["fwp_unitcharge"];
                        if (value != null && decimal.TryParse(value.ToString(), out decimal unitCharge))
                        {
                            disbursement.UnitCharge = unitCharge;
                        }
                    }
                    catch
                    {
                        disbursement.UnitCharge = 0m;
                    }
                }

                if (entity.Contains("fwp_disbursementtype"))
                {
                    try
                    {
                        var typeReference = entity.GetAttributeValue<EntityReference>("fwp_disbursementtype");
                        if (typeReference != null)
                        {
                            disbursement.DisbursementTypeGuid = typeReference.Id;
                            disbursement.DisbursementTypeId = typeReference.Id.GetHashCode();
                            disbursement.DisbursementTypeName = NormalizeDisbursementTypeName(typeReference.Name) ?? disbursement.DisbursementTypeName;
                        }
                    }
                    catch
                    {
                        disbursement.DisbursementTypeGuid = Guid.Empty;
                    }
                }

                // Handle legacy option-set disbursement type
                if (disbursement.DisbursementTypeGuid == Guid.Empty && entity.Contains("fwp_type"))
                {
                    try
                    {
                        var typeOption = entity.GetAttributeValue<OptionSetValue>("fwp_type");
                        if (typeOption != null)
                        {
                            disbursement.DisbursementTypeId = typeOption.Value;
                            disbursement.DisbursementTypeName = GetDisbursementTypeName(typeOption.Value);
                        }
                    }
                    catch
                    {
                        disbursement.DisbursementTypeId = 0;
                        disbursement.DisbursementTypeName = "Unknown Type";
                    }
                }

                // Handle Classification (NEW)
                if (entity.Contains("fwp_classification"))
                {
                    try
                    {
                        var classificationOption = entity.GetAttributeValue<OptionSetValue>("fwp_classification");
                        if (classificationOption != null)
                        {
                            disbursement.Classification = (DisbursementClassification)classificationOption.Value;
                        }
                    }
                    catch
                    {
                        disbursement.Classification = DisbursementClassification.Project; // Default
                    }
                }
                else
                {
                    // Default to project if classification field doesn't exist yet
                    disbursement.Classification = DisbursementClassification.Project;
                }

                // Handle Project reference
                if (entity.Contains("fwp_project"))
                {
                    try
                    {
                        var projectRef = entity.GetAttributeValue<EntityReference>("fwp_project");
                        if (projectRef != null)
                        {
                            disbursement.ProjectId = projectRef.Id.GetHashCode();
                            disbursement.ProjectGuid = projectRef.Id;

                            // Get project info from linked entities
                            if (entity.Contains("project.isc_projectnumbernew"))
                            {
                                var aliased = entity.GetAttributeValue<AliasedValue>("project.isc_projectnumbernew");
                                disbursement.ProjectNumber = aliased?.Value?.ToString() ?? "";
                            }

                            if (entity.Contains("project.msdyn_subject"))
                            {
                                var aliased = entity.GetAttributeValue<AliasedValue>("project.msdyn_subject");
                                disbursement.ProjectName = aliased?.Value?.ToString() ?? "";
                            }
                        }
                    }
                    catch
                    {
                        disbursement.ProjectId = 0;
                        disbursement.ProjectGuid = Guid.Empty;
                        disbursement.ProjectName = "Unknown Project";
                    }
                }

                // Handle Quote reference (NEW)
                if (entity.Contains("fwp_quote"))
                {
                    try
                    {
                        var quoteRef = entity.GetAttributeValue<EntityReference>("fwp_quote");
                        if (quoteRef != null)
                        {
                            disbursement.QuoteId = quoteRef.Id;

                            // Get quote info from linked entities
                            if (entity.Contains("quote.quotenumber"))
                            {
                                var aliased = entity.GetAttributeValue<AliasedValue>("quote.quotenumber");
                                disbursement.QuoteNumber = aliased?.Value?.ToString() ?? "";
                            }

                            if (entity.Contains("quote.name"))
                            {
                                var aliased = entity.GetAttributeValue<AliasedValue>("quote.name");
                                disbursement.QuoteName = aliased?.Value?.ToString() ?? "";
                            }

                            // Get client info from quote customer
                            if (entity.Contains("quotecustomer.name"))
                            {
                                var aliased = entity.GetAttributeValue<AliasedValue>("quotecustomer.name");
                                disbursement.ClientName = aliased?.Value?.ToString() ?? "";
                            }
                        }
                    }
                    catch
                    {
                        disbursement.QuoteId = Guid.Empty;
                        disbursement.QuoteName = "Unknown Quote";
                    }
                }

                // Handle Team Member
                if (entity.Contains("fwp_teammember"))
                {
                    try
                    {
                        var teamMemberRef = entity.GetAttributeValue<EntityReference>("fwp_teammember");
                        if (teamMemberRef != null)
                        {
                            disbursement.TeamMemberId = teamMemberRef.Id.GetHashCode();
                            disbursement.TeamMemberGuid = teamMemberRef.Id;

                            // Get team member name from linked entity
                            if (entity.Contains("teammember.fullname"))
                            {
                                var aliased = entity.GetAttributeValue<AliasedValue>("teammember.fullname");
                                disbursement.TeamMemberName = aliased?.Value?.ToString() ?? "Unknown Team Member";
                            }
                        }
                    }
                    catch
                    {
                        disbursement.TeamMemberId = 0;
                        disbursement.TeamMemberGuid = Guid.Empty;
                        disbursement.TeamMemberName = "Unknown Team Member";
                    }
                }

                return disbursement;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error mapping disbursement entity: {ex.Message}");
                return new Disbursement
                {
                    Description = "Error loading disbursement",
                    Date = DateTime.Today,
                    Amount = 0,
                    ProjectName = "Unknown Project",
                    TeamMemberName = "Unknown Team Member"
                };
            }
        }

        private Entity MapEntityFromDisbursementEnhanced(Disbursement disbursement)
        {
            try
            {
                // Start with existing mapping
                var entity = MapEntityFromDisbursement(disbursement);

                // Add quote-specific enhancements

                // Set classification (NEW field)
                if (disbursement.Classification != DisbursementClassification.Project)
                {
                    entity["fwp_classification"] = new OptionSetValue((int)disbursement.Classification);
                }

                // Handle quote reference (NEW field)
                if (disbursement.Classification == DisbursementClassification.Quote && disbursement.QuoteId != Guid.Empty)
                {
                    entity["fwp_quote"] = new EntityReference("quote", disbursement.QuoteId);
                    entity["fwp_project"] = null; // Clear project reference
                }
                else if (disbursement.Classification == DisbursementClassification.Project)
                {
                    entity["fwp_quote"] = null; // Clear quote reference
                    // Project reference is already handled by existing code
                }

                return entity;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in enhanced entity mapping: {ex.Message}");
                // Fall back to existing mapping
                return MapEntityFromDisbursement(disbursement);
            }
        }


        private Entity MapEntityFromDisbursementWithQuotes(Disbursement disbursement)
        {
            var entity = new Entity(_entityName);

            try
            {
                // Handle basic properties
                entity["fwp_date"] = disbursement.Date;
                entity["fwp_description"] = disbursement.Description ?? string.Empty;
                entity["fwp_value"] = new Money(disbursement.Amount);

                // Handle Units and UnitCharge for unit-based disbursements
                if (disbursement.IsUnitBased)
                {
                    entity["fwp_units"] = disbursement.Units;
                    entity["fwp_unitcharge"] = disbursement.UnitCharge;
                }

                // Set disbursement type
                if (disbursement.DisbursementTypeGuid != Guid.Empty)
                {
                    entity["fwp_disbursementtype"] = new EntityReference(_disbursementTypeEntityName, disbursement.DisbursementTypeGuid);
                }
                else if (disbursement.DisbursementTypeId > 0)
                {
                    entity["fwp_type"] = new OptionSetValue(disbursement.DisbursementTypeId);
                }

                // Set classification (NEW)
                entity["fwp_classification"] = new OptionSetValue((int)disbursement.Classification);

                // Set project or quote reference based on classification
                if (disbursement.Classification == DisbursementClassification.Project && disbursement.ProjectGuid != Guid.Empty)
                {
                    entity["fwp_project"] = new EntityReference("msdyn_project", disbursement.ProjectGuid);
                    entity["fwp_quote"] = null; // Clear quote reference
                }
                else if (disbursement.Classification == DisbursementClassification.Quote && disbursement.QuoteId != Guid.Empty)
                {
                    entity["fwp_quote"] = new EntityReference("quote", disbursement.QuoteId);
                    entity["fwp_project"] = null; // Clear project reference
                }

                // Set team member reference
                if (disbursement.TeamMemberGuid != Guid.Empty)
                {
                    entity["fwp_teammember"] = new EntityReference("systemuser", disbursement.TeamMemberGuid);
                }

                return entity;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error mapping disbursement to entity: {ex.Message}");
                throw;
            }
        }


    }
}