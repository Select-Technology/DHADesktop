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

                List<Entity> entities = _connector.RetrieveMultiple("fwp_timeentry", columns);
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