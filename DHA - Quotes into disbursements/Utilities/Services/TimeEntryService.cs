using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DHA.DSTC.WPF.Services
{
    public class TimeEntryService
    {
        private readonly DataverseConnector _connector;

        public TimeEntryService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public List<TimeEntry> GetTimeEntries()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("DEBUG: GetTimeEntries - Starting");

                // Ensure connection
                if (!_connector.Connect())
                {
                    System.Diagnostics.Debug.WriteLine("DEBUG: GetTimeEntries - Connection failed");
                    System.Windows.Forms.MessageBox.Show("Failed to connect to Dataverse",
                        "Connection Error", System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                    return new List<TimeEntry>();
                }

                System.Diagnostics.Debug.WriteLine("DEBUG: GetTimeEntries - Connected successfully");

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

                System.Diagnostics.Debug.WriteLine("DEBUG: GetTimeEntries - Columns defined");

                var links = new List<LinkEntity>();

                // First link: fwp_timeentry → msdyn_project (for project entries)
                var projectLink = new LinkEntity
                {
                    LinkFromEntityName = "fwp_timeentry",
                    LinkFromAttributeName = "fwp_project",
                    LinkToEntityName = "msdyn_project",
                    LinkToAttributeName = "msdyn_projectid",
                    Columns = new ColumnSet("isc_projectnumbernew", "msdyn_customer", "msdyn_subject"),
                    EntityAlias = "project",
                    JoinOperator = JoinOperator.LeftOuter // Use left outer join to include entries without projects
                };

                // Second link: msdyn_project → msdyn_customer (via EntityReference)
                var customerLink = new LinkEntity
                {
                    LinkFromEntityName = "msdyn_project",
                    LinkFromAttributeName = "msdyn_customer",
                    LinkToEntityName = "account", // Assuming customers are accounts
                    LinkToAttributeName = "accountid",
                    Columns = new ColumnSet("name"),
                    EntityAlias = "customer",
                    JoinOperator = JoinOperator.LeftOuter
                };

                // Add nested link
                projectLink.LinkEntities.Add(customerLink);

                // NEW: Third link: fwp_timeentry → quote (for quote entries)
                var quoteLink = new LinkEntity
                {
                    LinkFromEntityName = "fwp_timeentry",
                    LinkFromAttributeName = "fwp_quote",
                    LinkToEntityName = "quote",
                    LinkToAttributeName = "quoteid",
                    Columns = new ColumnSet("quotenumber", "name", "customerid"),
                    EntityAlias = "quote",
                    JoinOperator = JoinOperator.LeftOuter // Use left outer join to include entries without quotes
                };

                // NEW: Fourth link: quote → customer (for quote customer info)
                var quoteCustomerLink = new LinkEntity
                {
                    LinkFromEntityName = "quote",
                    LinkFromAttributeName = "customerid",
                    LinkToEntityName = "account", // Assuming customers are accounts
                    LinkToAttributeName = "accountid",
                    Columns = new ColumnSet("name"),
                    EntityAlias = "quotecustomer",
                    JoinOperator = JoinOperator.LeftOuter
                };

                // Add nested link for quote customer
                quoteLink.LinkEntities.Add(quoteCustomerLink);

                // Add to top-level links list
                links.Add(projectLink);
                links.Add(quoteLink);

                System.Diagnostics.Debug.WriteLine("DEBUG: GetTimeEntries - Links defined, calling RetrieveMultiple");

                List<Entity> entities = _connector.RetrieveMultiple("fwp_timeentry", columns, null, null, links);

                System.Diagnostics.Debug.WriteLine($"DEBUG: GetTimeEntries - Retrieved {entities?.Count ?? 0} entities from database");

                if (entities == null || entities.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("DEBUG: GetTimeEntries - No entities returned from database");
                    return new List<TimeEntry>();
                }

                // Log some details about the entities
                foreach (var entity in entities.Take(5)) // Just log first 5 for debugging
                {
                    var date = entity.GetAttributeValue<DateTime>("fwp_date");
                    var teamMember = entity.GetAttributeValue<EntityReference>("fwp_teammember");
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Entity {entity.Id} - Date: {date:yyyy-MM-dd}, TeamMember: {teamMember?.Id}");
                }

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
                    }
                }

                System.Diagnostics.Debug.WriteLine($"DEBUG: GetTimeEntries - Converted {timeEntries.Count} entries successfully, {conversionFailures} failed");

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
                if (!_connector.Connect())
                {
                    return Guid.Empty;
                }

                Entity entity = timeEntry.ToEntity();
                Guid newId = _connector._orgService.Create(entity);
                timeEntry.Id = newId;
                return newId;
            }
            catch (Exception ex)
            {
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
                if (!_connector.Connect())
                {
                    return false;
                }

                Entity entity = timeEntry.ToEntity();
                _connector._orgService.Update(entity);
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error updating time entry: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
        }

        public bool DeleteTimeEntry(Guid id)
        {
            try
            {
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