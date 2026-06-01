using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DHA.DSTC.WPF.Services
{
    public class DataverseService
    {
        private readonly DataverseConnector _connector;

        public DataverseService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public async Task<bool> ConnectAsync(bool forceReconnect = false)
        {
            try
            {
                if (_connector._orgService != null && !forceReconnect)
                    return true;

                return _connector.Connect();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error connecting to Dataverse: {ex.Message}",
                    "Connection Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
        }

        public async Task<List<Entity>> RetrieveMultipleEntitiesAsync(
            string entityName,
            ColumnSet columns,
            FilterExpression filter = null)
        {
            try
            {
                if (_connector._orgService == null)
                {
                    if (!await ConnectAsync())
                        return null;
                }

                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columns
                };

                if (filter != null)
                {
                    query.Criteria = filter;
                }

                var result = _connector._orgService.RetrieveMultiple(query);

                return result.Entities.ToList();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error retrieving data: {ex.Message}",
                    "Dataverse Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return null;
            }
        }

        public async Task<Entity> RetrieveEntityAsync(string entityName, Guid id, ColumnSet columns)
        {
            try
            {
                if (_connector._orgService == null)
                {
                    if (!await ConnectAsync())
                        return null;
                }

                return _connector._orgService.Retrieve(entityName, id, columns);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error retrieving entity: {ex.Message}",
                    "Dataverse Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return null;
            }
        }

        public async Task<Guid?> CreateEntityAsync(Entity entity)
        {
            try
            {
                if (_connector._orgService == null)
                {
                    if (!await ConnectAsync())
                    {
                        // Instead of using a conditional expression that returns int or null:
                        // return condition ? intValue : null;
                        // We'll use explicit returns:
                        return null;
                    }
                }

                return _connector._orgService.Create(entity);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error creating entity: {ex.Message}",
                    "Dataverse Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return null;
            }
        }

        public async Task<bool> UpdateEntityAsync(Entity entity)
        {
            try
            {
                if (_connector._orgService == null)
                {
                    if (!await ConnectAsync())
                        return false;
                }

                _connector._orgService.Update(entity);
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error updating entity: {ex.Message}",
                    "Dataverse Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
        }

        public async Task<bool> DeleteEntityAsync(string entityName, Guid id)
        {
            try
            {
                if (_connector._orgService == null)
                {
                    if (!await ConnectAsync())
                        return false;
                }

                _connector._orgService.Delete(entityName, id);
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error deleting entity: {ex.Message}",
                    "Dataverse Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
        }

        #region Project Methods

        public async Task<List<Project>> GetProjectsAsync(bool activeOnly = true)
        {
            var columns = new ColumnSet("msdyn_subject", "isc_projectnumbernew", "msdyn_customer", "isdisabled");

            var filter = new FilterExpression();
            if (activeOnly)
            {
                // Instead of using a conditional expression that can mix types:
                // activeOnly ? 0 : null
                // We'll use a more explicit approach:
                filter.AddCondition("isdisabled", ConditionOperator.Equal, true);
            }

            var entities = await RetrieveMultipleEntitiesAsync(
                "msdyn_project",
                columns,
                activeOnly ? filter : null); // This is safe because both sides are FilterExpression

            if (entities == null)
                return new List<Project>();

            return entities.Select(Project.FromEntity).ToList();
        }

        public async Task<Project> GetProjectAsync(Guid id)
        {
            var entity = await RetrieveEntityAsync(
                "msdyn_project",
                id,
                new ColumnSet("msdyn_subject", "isc_projectnumbernew", "msdyn_customer", "isdisabled"));

            return Project.FromEntity(entity);
        }

        public async Task<Guid?> SaveProjectAsync(Project project)
        {
            var entity = project.ToEntity();

            if (project.Id == Guid.Empty)
            {
                // For C# 7.3 compatibility, avoid conditional expressions that mix int and null
                Guid? newId = await CreateEntityAsync(entity);
                return newId; // This already returns Guid?
            }
            else
            {
                bool success = await UpdateEntityAsync(entity);
                // Instead of: return success ? project.Id : (Guid?)null;
                if (success)
                    return project.Id;
                return null;
            }
        }

        #endregion

        #region TeamMember Methods

        public async Task<List<TeamMember>> GetTeamMembersAsync(bool activeOnly = true)
        {
            var columns = new ColumnSet(
                "fullname",
                "internalemailaddress",
                "businessunitid",
                "statecode");

            var filter = new FilterExpression();
            if (activeOnly)
            {
                filter.AddCondition("statecode", ConditionOperator.Equal, 0);
            }

            // Add linked entity query to get business unit name
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = columns,
                Criteria = activeOnly ? filter : new FilterExpression()
            };

            var businessUnitLink = query.AddLink("businessunit", "businessunitid", "businessunitid");
            businessUnitLink.EntityAlias = "businessunitid";
            businessUnitLink.Columns = new ColumnSet("name");

            if (_connector._orgService == null)
            {
                if (!await ConnectAsync())
                    return new List<TeamMember>();
            }

            var result = _connector._orgService.RetrieveMultiple(query);
            return result.Entities.Select(TeamMember.FromEntity).ToList();
        }

        public async Task<TeamMember> GetTeamMemberAsync(Guid id)
        {
            if (_connector._orgService == null)
            {
                if (!await ConnectAsync())
                    return null;
            }

            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("fullname", "internalemailaddress", "businessunitid"),
                Criteria = new FilterExpression()
            };

            query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, id);

            // Add linked entity query to get business unit name
            var businessUnitLink = query.AddLink("businessunit", "businessunitid", "businessunitid");
            businessUnitLink.EntityAlias = "businessunitid";
            businessUnitLink.Columns = new ColumnSet("name");

            var result = _connector._orgService.RetrieveMultiple(query).Entities;

            if (result.Count > 0)
            {
                return TeamMember.FromEntity(result[0]);
            }

            return null;
        }

        #endregion

        #region TimeEntry Methods

        public async Task<List<TimeEntry>> GetTimeEntriesAsync(Guid teamMemberId)
        {
            var columns = new ColumnSet(
                "fwp_date",
                "fwp_hours",
                "fwp_minutes",
                "fwp_notes",
                "fwp_project",
                "fwp_teammember");

            var filter = new FilterExpression();
            filter.AddCondition("fwp_teammember", ConditionOperator.Equal, teamMemberId);

            var entities = await RetrieveMultipleEntitiesAsync(
                "fwp_timeentry",
                columns,
                filter);

            if (entities == null)
                return new List<TimeEntry>();

            return entities.Select(TimeEntry.FromEntity).ToList();
        }

        public async Task<TimeEntry> GetTimeEntryAsync(Guid id)
        {
            var entity = await RetrieveEntityAsync(
                "fwp_timeentry",
                id,
                new ColumnSet(
                    "fwp_date",
                    "fwp_hours",
                    "fwp_minutes",
                    "fwp_notes",
                    "fwp_project",
                    "fwp_teammember"));

            return TimeEntry.FromEntity(entity);
        }

        public async Task<Guid?> SaveTimeEntryAsync(TimeEntry timeEntry)
        {
            var entity = timeEntry.ToEntity();

            if (timeEntry.Id == Guid.Empty)
            {
                // Using explicit variable for C# 7.3 compatibility
                Guid? newId = await CreateEntityAsync(entity);
                return newId;
            }
            else
            {
                bool success = await UpdateEntityAsync(entity);
                // Instead of conditional expression mixing types
                if (success)
                    return timeEntry.Id;
                return null;
            }
        }

        #endregion

        public void Disconnect()
        {
            try
            {
                // Nothing to explicitly disconnect in this implementation
                // The CRM service client handles connection pooling
            }
            catch
            {
                // Ignore errors during disconnect
            }
        }
    }
}