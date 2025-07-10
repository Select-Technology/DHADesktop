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
                // Ensure connection
                if (!_connector.Connect())
                {
                    System.Windows.Forms.MessageBox.Show("Failed to connect to Dataverse",
                        "Connection Error", System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                    return new List<TimeEntry>();
                }

                // Only retrieve the fields we actually need - do NOT include fwp_quote
                var columns = new string[]
                {
                    "fwp_date",
                    "fwp_decimalhours",
                    "fwp_minutes",
                    "fwp_notes",
                    "fwp_project",
                    "fwp_teammember"
                };

                var links = new List<LinkEntity>();

                // First link: fwp_timeentry → msdyn_project
                var projectLink = new LinkEntity
                {
                    LinkFromEntityName = "fwp_timeentry",
                    LinkFromAttributeName = "fwp_project",
                    LinkToEntityName = "msdyn_project",
                    LinkToAttributeName = "msdyn_projectid",
                    Columns = new ColumnSet("isc_projectnumbernew", "msdyn_customer"),
                    EntityAlias = "project"
                };

                // Second link: msdyn_project → msdyn_customer (via EntityReference)
                var customerLink = new LinkEntity
                {
                    LinkFromEntityName = "msdyn_project",
                    LinkFromAttributeName = "msdyn_customer",
                    LinkToEntityName = "account", // or "contact" if your customer is stored there
                    LinkToAttributeName = "accountid", // or "contactid"
                    Columns = new ColumnSet("name"),
                    EntityAlias = "customer"
                };

                // Add nested link
                projectLink.LinkEntities.Add(customerLink);

                // Add to top-level links list
                links.Add(projectLink);

                List<Entity> entities = _connector.RetrieveMultiple("fwp_timeentry", columns, null, null, links);
                return entities.Select(TimeEntry.FromEntity).Where(te => te != null).ToList();
            }
            catch (Exception ex)
            {
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

                // Only retrieve essential columns
                var columns = new string[]
                {
                    "fwp_date",
                    "fwp_decimalhours",
                    "fwp_minutes",
                    "fwp_notes",
                    "fwp_project",
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

                // Validate required fields before creating
                if (timeEntry.ProjectId == Guid.Empty)
                {
                    throw new ArgumentException("Project is required for time entry");
                }

                if (timeEntry.TeamMemberId == Guid.Empty)
                {
                    throw new ArgumentException("Team member is required for time entry");
                }

                Entity entity = timeEntry.ToEntity();

                // Debug: Log what we're trying to create
                System.Diagnostics.Debug.WriteLine($"Creating time entry with {entity.Attributes.Count} attributes:");
                foreach (var attr in entity.Attributes)
                {
                    System.Diagnostics.Debug.WriteLine($"  {attr.Key}: {attr.Value}");
                }

                return _connector.Create(entity);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in CreateTimeEntry: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Re-throw with more context
                throw new Exception($"Failed to create time entry: {ex.Message}", ex);
            }
        }

        public void UpdateTimeEntry(TimeEntry timeEntry)
        {
            try
            {
                if (!_connector.Connect())
                {
                    return;
                }

                Entity entity = timeEntry.ToEntity();
                _connector.Update(entity);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error updating time entry: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        public void DeleteTimeEntry(Guid id)
        {
            try
            {
                if (!_connector.Connect())
                {
                    return;
                }

                _connector.Delete("fwp_timeentry", id);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error deleting time entry: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }
    }
}