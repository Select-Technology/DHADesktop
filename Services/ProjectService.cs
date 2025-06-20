using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace DHA.DSTC.WPF.Services
{
    public class ProjectService
    {
        private readonly DataverseConnector _connector;
        private readonly string _entityName = "msdyn_project";

        public ProjectService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public List<Project> GetProjects()
        {
            try
            {
                // Ensure connection before attempting to retrieve data
                if (!_connector.Connect())
                {
                    MessageBox.Show("Failed to connect to Dataverse",
                        "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return new List<Project>();
                }

                // Use specific columns instead of retrieving all
                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet("msdyn_subject", "isc_projectnumbernew", "msdyn_customer", "statecode"),
                    Orders = {
                        new OrderExpression("msdyn_subject", OrderType.Ascending)
                    }
                };

                // Add condition to only get active projects
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // 0 = Active

                var result = _connector._orgService.RetrieveMultiple(query);

                if (result?.Entities == null)
                {
                    MessageBox.Show("No data returned from Dataverse",
                        "Data Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return new List<Project>();
                }

                var entities = result.Entities.ToList();
                return entities.Select(Project.FromEntity).Where(p => p != null).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving projects: {ex.Message}\n\nEntity name: {_entityName}\n\nInner exception: {ex.InnerException?.Message}",
                    "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new List<Project>();
            }
        }

        public Project GetProject(Guid id)
        {
            try
            {
                if (!_connector.Connect())
                {
                    return null;
                }

                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet("msdyn_subject", "isc_projectnumbernew", "msdyn_customer", "statecode"),
                    Criteria = new FilterExpression()
                };

                query.Criteria.AddCondition("msdyn_projectid", ConditionOperator.Equal, id);

                var result = _connector._orgService.RetrieveMultiple(query);

                if (result?.Entities?.Count > 0)
                {
                    return Project.FromEntity(result.Entities[0]);
                }

                return null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving project: {ex.Message}",
                    "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        public List<Project> SearchProjects(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return GetProjects();
            }

            try
            {
                if (!_connector.Connect())
                {
                    return new List<Project>();
                }

                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet("msdyn_subject", "isc_projectnumbernew", "msdyn_customer", "statecode"),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };

                // Add search conditions
                var searchGroup = new FilterExpression(LogicalOperator.Or);
                searchGroup.AddCondition("msdyn_subject", ConditionOperator.Like, $"%{searchTerm}%");
                searchGroup.AddCondition("isc_projectnumbernew", ConditionOperator.Like, $"%{searchTerm}%");

                query.Criteria.AddFilter(searchGroup);

                // Only active projects
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

                var result = _connector._orgService.RetrieveMultiple(query);

                if (result?.Entities == null)
                {
                    return new List<Project>();
                }

                var entities = result.Entities.ToList();
                return entities.Select(Project.FromEntity).Where(p => p != null).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching projects: {ex.Message}",
                    "Search Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new List<Project>();
            }
        }

        public Guid CreateProject(Project project)
        {
            try
            {
                if (!_connector.Connect())
                {
                    return Guid.Empty;
                }

                Entity entity = project.ToEntity();
                return _connector.Create(entity);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating project: {ex.Message}",
                    "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return Guid.Empty;
            }
        }

        public void UpdateProject(Project project)
        {
            try
            {
                if (!_connector.Connect())
                {
                    return;
                }

                Entity entity = project.ToEntity();
                _connector.Update(entity);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating project: {ex.Message}",
                    "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void DeleteProject(Guid id)
        {
            try
            {
                if (!_connector.Connect())
                {
                    return;
                }

                _connector.Delete(_entityName, id);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting project: {ex.Message}",
                    "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}