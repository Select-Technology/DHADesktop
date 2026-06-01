using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Utilities;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DHA.DSTC.WPF.Services
{
    public class TimeEntryService
    {
        private readonly DataverseConnector _connector;

        public TimeEntryService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public List<TimeEntry> GetTimeEntries(Guid? teamMemberId = null,
            DateTime? queryFromDate = null, DateTime? queryToDate = null)
        {
            try
            {
                if (!_connector.Connect())
                {
                    System.Diagnostics.Debug.WriteLine("DEBUG: GetTimeEntries - Connection failed");
                    return new List<TimeEntry>();
                }

                System.Diagnostics.Debug.WriteLine($"DEBUG: GetTimeEntries - Starting query for user: {teamMemberId?.ToString() ?? "ALL USERS"}");

                // Use caller-supplied range when provided, otherwise fall back to last 90 days.
                // Add 1 extra day on each side to handle any UTC-offset edge cases.
                var fromDate = (queryFromDate ?? DateTime.Today.AddDays(-90)).AddDays(-1);
                var toDate   = (queryToDate   ?? DateTime.Today).AddDays(1);

                // CORRECTED: Only include fields that actually exist
                var columns = new string[]
                {
            "fwp_date",
            "fwp_decimalhours",    // ✅ Exists
            "fwp_minutes",         // ✅ Exists  
            "fwp_durationhours",   // ✅ Exists
            "fwp_notes",
            "fwp_category",
            "fwp_classification",
            "fwp_project",
            "fwp_quote",
            "fwp_teammember",
            "createdon"            // Add for CreatedDate
                };

                // Create base query with PROPER date filtering
                var query = new QueryExpression("fwp_timeentry")
                {
                    ColumnSet = new ColumnSet(columns),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };

                // CRITICAL FIX: Use date range instead of exact DateTime.Equal
                query.Criteria.AddCondition("fwp_date", ConditionOperator.GreaterEqual, fromDate);
                query.Criteria.AddCondition("fwp_date", ConditionOperator.LessThan, toDate);

                // ✅ NEW: Add user filter if provided (CRITICAL SECURITY & PERFORMANCE FIX)
                if (teamMemberId.HasValue && teamMemberId.Value != Guid.Empty)
                {
                    query.Criteria.AddCondition("fwp_teammember", ConditionOperator.Equal, teamMemberId.Value);
                    System.Diagnostics.Debug.WriteLine($"✅ Filtering for user: {teamMemberId.Value}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ WARNING: No user filter - retrieving ALL users' time entries!");
                }

                // Add order by for consistent results
                query.AddOrder("fwp_date", OrderType.Descending);
                query.AddOrder("createdon", OrderType.Descending);

                // Create LinkEntity for project information
                var projectLink = new LinkEntity
                {
                    LinkFromEntityName = "fwp_timeentry",
                    LinkFromAttributeName = "fwp_project",
                    LinkToEntityName = "msdyn_project",
                    LinkToAttributeName = "msdyn_projectid",
                    Columns = new ColumnSet("msdyn_subject", "isc_projectnumbernew", "msdyn_customer"),
                    EntityAlias = "project",
                    JoinOperator = JoinOperator.LeftOuter
                };

                // Create nested link for project customer
                var projectCustomerLink = new LinkEntity
                {
                    LinkFromEntityName = "msdyn_project",
                    LinkFromAttributeName = "msdyn_customer",
                    LinkToEntityName = "account",
                    LinkToAttributeName = "accountid",
                    Columns = new ColumnSet("name"),
                    EntityAlias = "customer",
                    JoinOperator = JoinOperator.LeftOuter
                };
                projectLink.LinkEntities.Add(projectCustomerLink);

                // Create LinkEntity for quote information  
                var quoteLink = new LinkEntity
                {
                    LinkFromEntityName = "fwp_timeentry",
                    LinkFromAttributeName = "fwp_quote",
                    LinkToEntityName = "quote",
                    LinkToAttributeName = "quoteid",
                    Columns = new ColumnSet("quotenumber", "name", "customerid"),
                    EntityAlias = "quote",
                    JoinOperator = JoinOperator.LeftOuter
                };

                // Create nested link for quote customer
                var quoteCustomerLink = new LinkEntity
                {
                    LinkFromEntityName = "quote",
                    LinkFromAttributeName = "customerid",
                    LinkToEntityName = "account",
                    LinkToAttributeName = "accountid",
                    Columns = new ColumnSet("name"),
                    EntityAlias = "quotecustomer",
                    JoinOperator = JoinOperator.LeftOuter
                };
                quoteLink.LinkEntities.Add(quoteCustomerLink);

                // Add links to query
                query.LinkEntities.Add(projectLink);
                query.LinkEntities.Add(quoteLink);

                System.Diagnostics.Debug.WriteLine($"DEBUG: GetTimeEntries - Querying from {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}");

                // Execute query
                var result = _connector._orgService.RetrieveMultiple(query);
                var entities = result?.Entities?.ToList() ?? new List<Entity>();

                System.Diagnostics.Debug.WriteLine($"DEBUG: GetTimeEntries - Retrieved {entities.Count} entities from database");

                if (entities.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("DEBUG: GetTimeEntries - No entities returned from database");
                    return new List<TimeEntry>();
                }

                // Log some details about the retrieved entities
                var todayCount = entities.Count(e =>
                {
                    if (e.Contains("fwp_date"))
                    {
                        var date = e.GetAttributeValue<DateTime>("fwp_date");
                        return date.Date == DateTime.Today;
                    }
                    return false;
                });

                System.Diagnostics.Debug.WriteLine($"DEBUG: GetTimeEntries - Found {todayCount} entries for today");

                // Convert entities to TimeEntry objects
                var timeEntries = new List<TimeEntry>();
                var conversionFailures = 0;

                foreach (var entity in entities)
                {
                    try
                    {
                        var timeEntry = TimeEntry.FromEntity(entity);
                        if (timeEntry != null)
                        {
                            timeEntries.Add(timeEntry);
                        }
                        else
                        {
                            conversionFailures++;
                            System.Diagnostics.Debug.WriteLine($"DEBUG: TimeEntry.FromEntity returned null for entity {entity.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        conversionFailures++;
                        System.Diagnostics.Debug.WriteLine($"DEBUG: Error converting entity {entity.Id}: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"  Exception details: {ex.StackTrace}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"DEBUG: GetTimeEntries - Converted {timeEntries.Count} entries successfully, {conversionFailures} failed");

                // Final verification for today's entries
                var todayEntries = timeEntries.Where(te => te.Date.Date == DateTime.Today).ToList();
                System.Diagnostics.Debug.WriteLine($"DEBUG: GetTimeEntries - Final result contains {todayEntries.Count} entries for today");

                if (todayEntries.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine("DEBUG: Today's entries:");
                    foreach (var entry in todayEntries.Take(3))
                    {
                        System.Diagnostics.Debug.WriteLine($"  • {entry.Comments} - {entry.TeamMemberId} - {entry.TotalHours}h");
                    }
                }

                return timeEntries;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DEBUG: GetTimeEntries - Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"DEBUG: GetTimeEntries - Stack trace: {ex.StackTrace}");
                System.Windows.Forms.MessageBox.Show($"Error retrieving time entries: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return new List<TimeEntry>();
            }
        }

        public async Task<ReconciliationValidationResult> UpdateTimeEntryAsync(TimeEntry timeEntry)
        {
            try
            {
                // CRITICAL: Check reconciliation status before allowing updates
                var reconciliationValidation = ReconciliationValidationHelper.ValidateTimeEntryModification(
                    timeEntry.IdGuid, _connector);

                if (!reconciliationValidation.IsValid)
                {
                    return reconciliationValidation;
                }

                if (!_connector.Connect())
                {
                    return ReconciliationValidationResult.Error("Unable to connect to Dataverse.");
                }

                var entity = timeEntry.ToEntity();
                _connector._orgService.Update(entity);

                System.Diagnostics.Debug.WriteLine($"✓ Time entry {timeEntry.IdGuid} updated successfully");
                return ReconciliationValidationResult.Success();
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error updating time entry: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(errorMessage);
                return ReconciliationValidationResult.Error(errorMessage);
            }
        }

        public async Task<ReconciliationValidationResult> DeleteTimeEntryAsync(Guid timeEntryId)
        {
            try
            {
                // CRITICAL: Check reconciliation status before allowing deletion
                var reconciliationValidation = ReconciliationValidationHelper.ValidateTimeEntryDeletion(
                    timeEntryId, _connector);

                if (!reconciliationValidation.IsValid)
                {
                    return reconciliationValidation;
                }

                if (!_connector.Connect())
                {
                    return ReconciliationValidationResult.Error("Unable to connect to Dataverse.");
                }

                _connector._orgService.Delete("fwp_timeentry", timeEntryId);

                System.Diagnostics.Debug.WriteLine($"✓ Time entry {timeEntryId} deleted successfully");
                return ReconciliationValidationResult.Success();
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error deleting time entry: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(errorMessage);
                return ReconciliationValidationResult.Error(errorMessage);
            }
        }



        // In TimeEntryService.cs - GetAllTimeEntriesAsync method
        // Replace your existing GetAllTimeEntriesAsync method in TimeEntryService.cs with this fixed version
        public async Task<List<TimeEntry>> GetAllTimeEntriesAsync()
        {
            try
            {
                _connector.Connect();

                // Start with a simple base query that ALWAYS works
                var baseQuery = new QueryExpression("fwp_timeentry")
                {
                    ColumnSet = new ColumnSet(true),
                    Orders = { new OrderExpression("fwp_date", OrderType.Descending) }
                };

                List<Entity> entities = new List<Entity>();

                // Try enhanced query with joins, but fall back gracefully if it fails
                try
                {
                    System.Diagnostics.Debug.WriteLine("Attempting enhanced query with joins...");

                    var enhancedQuery = new QueryExpression("fwp_timeentry")
                    {
                        ColumnSet = new ColumnSet(true),
                        Orders = { new OrderExpression("fwp_date", OrderType.Descending) }
                    };

                    // Add joins one by one with individual error handling
                    try
                    {
                        var projectLink = enhancedQuery.AddLink("msdyn_project", "fwp_project", "msdyn_projectid", JoinOperator.LeftOuter);
                        projectLink.EntityAlias = "project";
                        projectLink.Columns = new ColumnSet("msdyn_subject", "isc_projectnumbernew");
                    }
                    catch (Exception projEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Project join failed, continuing without: {projEx.Message}");
                    }

                    try
                    {
                        var quoteLink = enhancedQuery.AddLink("quote", "fwp_quote", "quoteid", JoinOperator.LeftOuter);
                        quoteLink.EntityAlias = "quote";
                        quoteLink.Columns = new ColumnSet("name", "quotenumber");
                    }
                    catch (Exception quoteEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Quote join failed, continuing without: {quoteEx.Message}");
                    }

                    // SKIP the problematic team member join that's causing metadata errors
                    // We'll handle team member names in post-processing instead

                    entities = _connector._orgService.RetrieveMultiple(enhancedQuery).Entities.ToList();
                    System.Diagnostics.Debug.WriteLine($"✅ Enhanced query succeeded with {entities.Count} entries");
                }
                catch (Exception enhancedEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Enhanced query failed: {enhancedEx.Message}");
                    System.Diagnostics.Debug.WriteLine("Falling back to simple query...");

                    // Fallback to simple query to ensure we don't lose any entries
                    entities = _connector._orgService.RetrieveMultiple(baseQuery).Entities.ToList();
                    System.Diagnostics.Debug.WriteLine($"✅ Simple query fallback returned {entities.Count} entries");
                }

                if (entities.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ No entities returned from any query method");
                }

                // Convert entities to TimeEntry objects with error handling
                var timeEntries = new List<TimeEntry>();
                int conversionFailures = 0;

                foreach (var entity in entities)
                {
                    try
                    {
                        var timeEntry = TimeEntry.FromEntity(entity);
                        if (timeEntry != null)
                        {
                            timeEntries.Add(timeEntry);
                        }
                        else
                        {
                            conversionFailures++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Entity conversion failed: {ex.Message}");
                        conversionFailures++;
                    }
                }

                if (conversionFailures > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ {conversionFailures} entities failed to convert");
                }

                System.Diagnostics.Debug.WriteLine($"GetAllTimeEntriesAsync: Final result = {timeEntries.Count} time entries");
                return timeEntries;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Critical error in GetAllTimeEntriesAsync: {ex.Message}");
                throw; // Re-throw to preserve stack trace
            }
        }

        public async Task<bool> DiagnoseMetadataIssueAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== METADATA DIAGNOSIS START ===");

                if (!_connector.Connect())
                {
                    System.Diagnostics.Debug.WriteLine("❌ Connection failed");
                    return false;
                }

                // Test 1: Check if entities exist
                var testEntities = new[] { "fwp_timeentry", "fwp_disbursement", "systemuser" };

                foreach (var entityName in testEntities)
                {
                    try
                    {
                        var testQuery = new QueryExpression(entityName)
                        {
                            ColumnSet = new ColumnSet("createdon"),
                            TopCount = 1
                        };

                        var result = _connector._orgService.RetrieveMultiple(testQuery);
                        System.Diagnostics.Debug.WriteLine($"✓ Entity '{entityName}' accessible - {result.Entities.Count} records");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Entity '{entityName}' error: {ex.Message}");
                    }
                }

                // Test 2: Check specific fields
                try
                {
                    var fieldTestQuery = new QueryExpression("fwp_timeentry")
                    {
                        ColumnSet = new ColumnSet("fwp_teammember"),
                        TopCount = 1
                    };

                    var fieldResult = _connector._orgService.RetrieveMultiple(fieldTestQuery);
                    System.Diagnostics.Debug.WriteLine($"✓ Field 'fwp_teammember' accessible on fwp_timeentry");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Field 'fwp_teammember' error: {ex.Message}");
                }

                // Test 3: Simple join test
                try
                {
                    var joinTestQuery = new QueryExpression("fwp_timeentry")
                    {
                        ColumnSet = new ColumnSet("createdon"),
                        TopCount = 1
                    };

                    var link = joinTestQuery.AddLink("systemuser", "fwp_teammember", "systemuserid", JoinOperator.LeftOuter);
                    link.EntityAlias = "user";
                    link.Columns = new ColumnSet("fullname");

                    var joinResult = _connector._orgService.RetrieveMultiple(joinTestQuery);
                    System.Diagnostics.Debug.WriteLine($"✓ Join from fwp_timeentry to systemuser works");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Join test failed: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"❌ THIS IS YOUR METADATA CACHE ERROR: {ex}");
                }

                System.Diagnostics.Debug.WriteLine("=== METADATA DIAGNOSIS END ===");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Metadata diagnosis failed: {ex.Message}");
                return false;
            }
        }


        private async Task<List<TimeEntry>> GetTimeEntriesSimpleAsync()
        {
            try
            {
                var query = new QueryExpression("fwp_timeentry")
                {
                    ColumnSet = new ColumnSet("fwp_date", "fwp_description", "fwp_decimalhours",
                                            "fwp_minutes", "fwp_notes", "fwp_category",
                                            "fwp_classification", "fwp_project", "fwp_quote",
                                            "fwp_teammember"),
                    Orders = { new OrderExpression("fwp_date", OrderType.Descending) }
                };

                // **FIXED: Use _connector._orgService instead of _orgService**
                var entities = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
                return entities.Select(TimeEntry.FromEntity).Where(te => te != null).ToList();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Critical error retrieving time entries: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return new List<TimeEntry>();
            }
        }

        // **BONUS: Environment validation method with proper _orgService access**
        public async Task<bool> ValidateDataverseEnvironmentAsync()
        {
            try
            {
                if (!_connector.Connect())
                {
                    return false;
                }

                // Verify critical entities exist
                var requiredEntities = new[] { "fwp_timeentry", "fwp_disbursement", "systemuser", "msdyn_project", "quote" };

                foreach (var entityName in requiredEntities)
                {
                    try
                    {
                        var request = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest()
                        {
                            LogicalName = entityName,
                            EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes
                        };

                        // **FIXED: Use _connector._orgService instead of _orgService**
                        var response = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)_connector._orgService.Execute(request);
                        System.Diagnostics.Debug.WriteLine($"✓ Entity '{entityName}' found with {response.EntityMetadata.Attributes.Length} attributes");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"✗ Entity '{entityName}' issue: {ex.Message}");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Environment validation failed: {ex.Message}");
                return false;
            }
        }

        public TimeEntry GetTimeEntry(Guid id)
        {
            try
            {
                if (!_connector.Connect())
                {
                    return null;
                }

                // Include the new fields in the column set
                var columns = new string[]
                {
                    "fwp_date",
                    "fwp_decimalhours",
                    "fwp_minutes",
                    "fwp_notes",
                    "fwp_category",
                    "fwp_classification", // NEW: Add classification field
                    "fwp_project",
                    "fwp_quote", // NEW: Add quote field
                    "fwp_teammember"
                };

                Entity entity = _connector.Retrieve("fwp_timeentry", id, columns);
                return TimeEntry.FromEntity(entity);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error retrieving time entry: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return null;
            }
        }

        public Guid CreateTimeEntry(TimeEntry timeEntry)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"");
                System.Diagnostics.Debug.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"▶ CREATE TIME ENTRY OPERATION STARTING");
                System.Diagnostics.Debug.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");

                if (!_connector.Connect())
                {
                    System.Diagnostics.Debug.WriteLine($"❌ CREATE FAILED: Connection to Dataverse failed");
                    return Guid.Empty;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Connected to Dataverse");
                System.Diagnostics.Debug.WriteLine($"Converting TimeEntry to Dataverse Entity...");

                Entity entity = timeEntry.ToEntity();

                System.Diagnostics.Debug.WriteLine($"Calling Dataverse Create operation...");
                Guid newId = _connector._orgService.Create(entity);
                timeEntry.Id = newId;

                System.Diagnostics.Debug.WriteLine($"");
                System.Diagnostics.Debug.WriteLine($"✅ TIME ENTRY CREATED SUCCESSFULLY");
                System.Diagnostics.Debug.WriteLine($"   New ID: {newId}");
                System.Diagnostics.Debug.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"");

                // Verify what was actually stored
                VerifyTimeEntryInDataverse(newId, "CREATE");

                return newId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"");
                System.Diagnostics.Debug.WriteLine($"❌ CREATE TIME ENTRY FAILED");
                System.Diagnostics.Debug.WriteLine($"   Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   Stack: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"");

                System.Windows.Forms.MessageBox.Show($"Error creating time entry: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return Guid.Empty;
            }
        }

        public bool UpdateTimeEntry(TimeEntry timeEntry)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"");
                System.Diagnostics.Debug.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"▶ UPDATE TIME ENTRY OPERATION STARTING");
                System.Diagnostics.Debug.WriteLine($"   Entry ID: {timeEntry.IdGuid}");
                System.Diagnostics.Debug.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");

                var reconciliationValidation = ReconciliationValidationHelper.ValidateTimeEntryModification(
            timeEntry.IdGuid, _connector);

                if (!reconciliationValidation.IsValid)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ UPDATE BLOCKED: Reconciliation validation failed");
                    System.Diagnostics.Debug.WriteLine($"   Reason: {reconciliationValidation.Message}");

                    // Show reconciliation error to user
                    System.Windows.Forms.MessageBox.Show(reconciliationValidation.Message,
                        "Cannot Modify Time Entry",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Reconciliation validation passed");

                if (!_connector.Connect())
                {
                    System.Diagnostics.Debug.WriteLine($"❌ UPDATE FAILED: Connection to Dataverse failed");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Connected to Dataverse");
                System.Diagnostics.Debug.WriteLine($"Converting TimeEntry to Dataverse Entity...");

                Entity entity = timeEntry.ToEntity();

                System.Diagnostics.Debug.WriteLine($"Calling Dataverse Update operation...");
                _connector._orgService.Update(entity);

                System.Diagnostics.Debug.WriteLine($"");
                System.Diagnostics.Debug.WriteLine($"✅ TIME ENTRY UPDATED SUCCESSFULLY");
                System.Diagnostics.Debug.WriteLine($"   Entry ID: {timeEntry.IdGuid}");
                System.Diagnostics.Debug.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"");

                // Verify what was actually stored after update
                VerifyTimeEntryInDataverse(timeEntry.IdGuid, "UPDATE");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"");
                System.Diagnostics.Debug.WriteLine($"❌ UPDATE TIME ENTRY FAILED");
                System.Diagnostics.Debug.WriteLine($"   Entry ID: {timeEntry.IdGuid}");
                System.Diagnostics.Debug.WriteLine($"   Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   Stack: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"");

                System.Windows.Forms.MessageBox.Show($"Error updating time entry: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// Verifies what data is actually stored in Dataverse for a time entry.
        /// Call this after Create or Update to see what Dataverse actually has.
        /// </summary>
        public void VerifyTimeEntryInDataverse(Guid timeEntryId, string operationType = "OPERATION")
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"");
                System.Diagnostics.Debug.WriteLine($"╔═══════════════════════════════════════════════════════════════════════════════╗");
                System.Diagnostics.Debug.WriteLine($"║ DATAVERSE VERIFICATION - AFTER {operationType}                               ");
                System.Diagnostics.Debug.WriteLine($"╠═══════════════════════════════════════════════════════════════════════════════╣");
                System.Diagnostics.Debug.WriteLine($"║ Entry ID: {timeEntryId}");

                if (!_connector.Connect())
                {
                    System.Diagnostics.Debug.WriteLine($"║ ❌ Cannot verify - connection failed");
                    System.Diagnostics.Debug.WriteLine($"╚═══════════════════════════════════════════════════════════════════════════════╝");
                    return;
                }

                // Retrieve ALL fields to see what's actually stored
                var entity = _connector._orgService.Retrieve("fwp_timeentry", timeEntryId, new ColumnSet(true));

                System.Diagnostics.Debug.WriteLine($"║ ");
                System.Diagnostics.Debug.WriteLine($"║ STORED VALUES IN DATAVERSE:");
                System.Diagnostics.Debug.WriteLine($"║ ─────────────────────────────────────────────────────────────────────────────");

                foreach (var attr in entity.Attributes.OrderBy(a => a.Key))
                {
                    var value = attr.Value;
                    string displayValue;

                    if (value == null)
                    {
                        displayValue = "(null)";
                    }
                    else if (value is EntityReference entityRef)
                    {
                        displayValue = $"EntityRef: {entityRef.LogicalName} - {entityRef.Id}";
                        if (!string.IsNullOrEmpty(entityRef.Name))
                            displayValue += $" ({entityRef.Name})";
                    }
                    else if (value is OptionSetValue optionSet)
                    {
                        displayValue = $"OptionSet: {optionSet.Value}";
                    }
                    else if (value is Money money)
                    {
                        displayValue = $"Money: {money.Value:C}";
                    }
                    else if (value is DateTime dateTime)
                    {
                        displayValue = $"{dateTime:yyyy-MM-dd HH:mm:ss} (Kind: {dateTime.Kind})";
                    }
                    else
                    {
                        displayValue = value.ToString();
                    }

                    System.Diagnostics.Debug.WriteLine($"║ {attr.Key}: {displayValue}");
                }

                // Specifically highlight the key fields
                System.Diagnostics.Debug.WriteLine($"║ ");
                System.Diagnostics.Debug.WriteLine($"║ KEY FIELD SUMMARY:");
                System.Diagnostics.Debug.WriteLine($"║ ─────────────────────────────────────────────────────────────────────────────");

                if (entity.Contains("fwp_decimalhours"))
                    System.Diagnostics.Debug.WriteLine($"║ ⏱️  fwp_decimalhours: {entity.GetAttributeValue<decimal>("fwp_decimalhours")}");

                if (entity.Contains("fwp_durationhours"))
                    System.Diagnostics.Debug.WriteLine($"║ ⏱️  fwp_durationhours: {entity.GetAttributeValue<decimal>("fwp_durationhours")}");

                if (entity.Contains("fwp_minutes"))
                    System.Diagnostics.Debug.WriteLine($"║ ⏱️  fwp_minutes: {entity.GetAttributeValue<int>("fwp_minutes")}");

                if (entity.Contains("fwp_value"))
                {
                    var moneyValue = entity.GetAttributeValue<Money>("fwp_value");
                    System.Diagnostics.Debug.WriteLine($"║ 💰 fwp_value: {moneyValue?.Value:C} ⚠️ UNEXPECTED FOR TIME ENTRY!");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"║ 💰 fwp_value: NOT SET ✅ (correct for time entry)");
                }

                System.Diagnostics.Debug.WriteLine($"╚═══════════════════════════════════════════════════════════════════════════════╝");
                System.Diagnostics.Debug.WriteLine($"");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"║ ❌ Verification failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"╚═══════════════════════════════════════════════════════════════════════════════╝");
                System.Diagnostics.Debug.WriteLine($"");
            }
        }

        public bool DeleteTimeEntry(Guid id)
        {
            try
            {
                var reconciliationValidation = ReconciliationValidationHelper.ValidateTimeEntryDeletion(
            id, _connector);

                if (!reconciliationValidation.IsValid)
                {
                    System.Windows.Forms.MessageBox.Show(reconciliationValidation.Message,
                        "Cannot Delete Time Entry",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                    return false;
                }

                if (!_connector.Connect())
                {
                    return false;
                }

                _connector._orgService.Delete("fwp_timeentry", id);
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error deleting time entry: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
        }
    }
}