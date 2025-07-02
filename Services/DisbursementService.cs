using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.DSTC.WPF.Services
{
    public class DisbursementService
    {
        private readonly DataverseConnector _connector;
        private readonly string _entityName = "fwp_disbursement"; // Update this to match your actual entity name

        public DisbursementService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public async Task<List<Disbursement>> GetAllDisbursementsAsync()
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
                        Name = o.Label.UserLocalizedLabel.Label,
                        Description = o.Description?.UserLocalizedLabel?.Label ?? o.Label.UserLocalizedLabel.Label,
                        UnitCharge = 0 // Will be populated below
                    }).ToList();

                    // Populate unit charges from configuration entity or hardcoded values
                    await PopulateDisbursementTypeUnitCharges(disbursementTypes);

                    return disbursementTypes;
                }

                // If we get here, we couldn't find the metadata or it wasn't the expected type
                // Fall back to our default values as a last resort
                return GetDefaultDisbursementTypes();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error retrieving disbursement types: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);

                // Return default values as fallback
                return GetDefaultDisbursementTypes();
            }
        }

        private async Task PopulateDisbursementTypeUnitCharges(List<DisbursementType> disbursementTypes)
        {
            try
            {
                // Try to get unit charges from a configuration entity if it exists
                // If not, fall back to hardcoded values based on type names

                // Hardcoded unit charges for now - you can replace this with a database query later
                var unitCharges = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Mileage", 0.65m },
                    { "Photocopying (B&W)", 0.10m },
                    { "Photocopying (Colour)", 0.25m },
                    // Add more as needed
                };

                foreach (var disbursementType in disbursementTypes)
                {
                    if (unitCharges.TryGetValue(disbursementType.Name, out decimal unitCharge))
                    {
                        disbursementType.UnitCharge = unitCharge;
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
                new DisbursementType { Id = 800470004, Name = "Printing", Description = "Printing costs", UnitCharge = 0 },
                new DisbursementType { Id = 800470005, Name = "Photocopying (B&W)", Description = "Black and white photocopying", UnitCharge = 0.10m },
                new DisbursementType { Id = 800470006, Name = "Photocopying (Colour)", Description = "Colour photocopying", UnitCharge = 0.25m },
                new DisbursementType { Id = 800470007, Name = "Parking", Description = "Parking fees", UnitCharge = 0 },
                new DisbursementType { Id = 800470008, Name = "Postage", Description = "Postage costs", UnitCharge = 0 },
                new DisbursementType { Id = 800470009, Name = "Subcontract Professional Services", Description = "Professional services from subcontractors", UnitCharge = 0 },
                new DisbursementType { Id = 800470010, Name = "Using Default", Description = "Using Default", UnitCharge = 0 }
            };
        }

        private string GetDisbursementTypeName(int optionValue)
        {
            // Get matching type from your default list
            var defaultTypes = GetDefaultDisbursementTypes();
            var matchingType = defaultTypes.FirstOrDefault(t => t.Id == optionValue);
            return matchingType?.Name ?? $"Type {optionValue}";
        }

        private Disbursement MapDisbursementFromEntity(Entity entity)
        {
            if (entity == null)
                return null;

            try
            {
                var disbursement = new Disbursement
                {
                    Id = entity.Id.GetHashCode(), // Convert Guid to int for backwards compatibility
                    IdGuid = entity.Id // Store the original Guid for future operations
                };

                // Handle Date with fallback to today if missing or invalid
                if (entity.Contains("fwp_date"))
                {
                    disbursement.Date = entity.GetAttributeValue<DateTime>("fwp_date");
                    // Double check for min value which can cause issues
                    if (disbursement.Date == DateTime.MinValue)
                        disbursement.Date = DateTime.Today;
                }
                else
                {
                    disbursement.Date = DateTime.Today;
                }

                // Handle Description with null checking
                disbursement.Description = entity.GetAttributeValue<string>("fwp_description") ?? string.Empty;

                // Handle Money type more carefully
                if (entity.Contains("fwp_value"))
                {
                    Money moneyValue = entity.GetAttributeValue<Money>("fwp_value");
                    if (moneyValue != null)
                    {
                        disbursement.Amount = moneyValue.Value;
                    }
                    else
                    {
                        disbursement.Amount = 0m;
                    }
                }
                else
                {
                    disbursement.Amount = 0m;
                }

                // Handle Units and UnitCharge if they exist
                if (entity.Contains("fwp_units"))
                {
                    disbursement.Units = entity.GetAttributeValue<decimal>("fwp_units");
                }

                if (entity.Contains("fwp_unitcharge"))
                {
                    disbursement.UnitCharge = entity.GetAttributeValue<decimal>("fwp_unitcharge");
                }

                // Handle Project reference
                if (entity.Contains("fwp_project"))
                {
                    var projectRef = entity.GetAttributeValue<EntityReference>("fwp_project");
                    if (projectRef != null)
                    {
                        disbursement.ProjectId = projectRef.Id.GetHashCode(); // Convert Guid to int
                        disbursement.ProjectGuid = projectRef.Id; // Store original Guid
                        disbursement.ProjectName = projectRef.Name ?? "Unknown Project";
                    }
                }

                // Handle Disbursement Type as OptionSetValue, not EntityReference
                if (entity.Contains("fwp_type"))
                {
                    var typeOption = entity.GetAttributeValue<OptionSetValue>("fwp_type");
                    if (typeOption != null)
                    {
                        disbursement.DisbursementTypeId = typeOption.Value;
                        disbursement.DisbursementTypeName = GetDisbursementTypeName(typeOption.Value);
                    }
                }

                // Handle Team Member reference
                if (entity.Contains("fwp_teammember"))
                {
                    var teamMemberRef = entity.GetAttributeValue<EntityReference>("fwp_teammember");
                    if (teamMemberRef != null)
                    {
                        disbursement.TeamMemberId = teamMemberRef.Id.GetHashCode(); // Convert Guid to int
                        disbursement.TeamMemberGuid = teamMemberRef.Id; // Store original Guid
                        disbursement.TeamMemberName = teamMemberRef.Name ?? "Unknown Team Member";
                    }
                }

                return disbursement;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error mapping disbursement entity: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return new Disbursement { Description = "Error loading disbursement" };
            }
        }

        private Entity MapEntityFromDisbursement(Disbursement disbursement)
        {
            var entity = new Entity(_entityName);

            try
            {
                // Handle basic properties
                entity["fwp_date"] = disbursement.Date;
                entity["fwp_description"] = disbursement.Description ?? string.Empty;

                // Handle Money value - ensure it's a valid decimal
                entity["fwp_value"] = new Money(disbursement.Amount);

                // Handle Units and UnitCharge for unit-based disbursements
                if (disbursement.IsUnitBased)
                {
                    entity["fwp_units"] = disbursement.Units;
                    entity["fwp_unitcharge"] = disbursement.UnitCharge;
                }

                // For OptionSetValue type fields - use the correct type
                if (disbursement.DisbursementTypeId > 0)
                {
                    entity["fwp_type"] = new OptionSetValue(disbursement.DisbursementTypeId);
                }

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
                        System.Diagnostics.Debug.WriteLine(
                            $"Warning: Could not convert Project ID: {ex.Message}");
                    }
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
                        // Only as fallback
                        entity["fwp_teammember"] = new EntityReference(
                            "systemuser",
                            Utilities.GuidConverter.GetDeterministicGuidFromId(disbursement.TeamMemberId));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Warning: Could not convert Team Member ID: {ex.Message}");
                    }
                }

                return entity;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error creating disbursement entity: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return entity; // Return whatever we have
            }
        }
    }
}