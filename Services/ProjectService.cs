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
                    ColumnSet = new ColumnSet(
                        "msdyn_subject",
                        "isc_projectnumbernew",
                        "msdyn_customer",
                        "statuscode"
                    ),
                    Orders = {
                        new OrderExpression("msdyn_subject", OrderType.Ascending)
                    }
                };

                // Explicitly filter out completed projects
                var statusFilter = new FilterExpression(LogicalOperator.And);
                statusFilter.AddCondition("statuscode", ConditionOperator.NotEqual, 192350001);
                query.Criteria = statusFilter;

                // Increase page size to get more projects initially
                query.PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1
                };

                var result = _connector._orgService.RetrieveMultiple(query);

                if (result?.Entities == null)
                {
                    MessageBox.Show("No data returned from Dataverse",
                        "Data Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return new List<Project>();
                }

                var entities = result.Entities.ToList();
                var projects = entities
                    .Select(Project.FromEntity)
                    .Where(p => p != null && p.IsActive)
                    .ToList();

                // Debug logging
                System.Diagnostics.Debug.WriteLine($"Retrieved {entities.Count} total entities, {projects.Count} active projects");

                return projects;
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
                    ColumnSet = new ColumnSet(
                        "msdyn_subject",
                        "isc_projectnumbernew",
                        "msdyn_customer",
                        "statuscode"
                    ),
                    Criteria = new FilterExpression()
                };

                // Explicitly filter out completed projects
                query.Criteria.AddCondition("statuscode", ConditionOperator.NotEqual, 192350001);
                query.Criteria.AddCondition("msdyn_projectid", ConditionOperator.Equal, id);

                var result = _connector._orgService.RetrieveMultiple(query);

                if (result?.Entities?.Count > 0)
                {
                    var project = Project.FromEntity(result.Entities[0]);
                    return project?.IsActive == true ? project : null;
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
                    ColumnSet = new ColumnSet(
                        "msdyn_subject",
                        "isc_projectnumbernew",
                        "msdyn_customer",
                        "statuscode"
                    ),
                    Criteria = new FilterExpression(LogicalOperator.And),
                    Orders = {
                        new OrderExpression("msdyn_subject", OrderType.Ascending)
                    }
                };

                // Explicitly filter out completed projects
                var statusFilter = new FilterExpression(LogicalOperator.And);
                statusFilter.AddCondition("statuscode", ConditionOperator.NotEqual, 192350001);
                query.Criteria.AddFilter(statusFilter);

                // Create the main search filter group using OR logic
                var searchGroup = new FilterExpression(LogicalOperator.Or);

                // Always search the full term against project name (msdyn_subject)
                searchGroup.AddCondition("msdyn_subject", ConditionOperator.Like, $"%{searchTerm}%");

                // Check if search term starts with a 5-9 digit number for enhanced project number search
                var projectNumberMatch = System.Text.RegularExpressions.Regex.Match(
                    searchTerm.Trim(),
                    @"^(\d{5,9})"
                );

                if (projectNumberMatch.Success)
                {
                    // Extract the project number (5-9 digits)
                    string projectNumber = projectNumberMatch.Groups[1].Value;

                    // Search for this number in the project number field using both Equal and Like
                    searchGroup.AddCondition("isc_projectnumbernew", ConditionOperator.Equal, projectNumber);
                    searchGroup.AddCondition("isc_projectnumbernew", ConditionOperator.Like, $"%{projectNumber}%");

                    // Also search for this number in the project name (in case it's stored there)
                    searchGroup.AddCondition("msdyn_subject", ConditionOperator.Like, $"%{projectNumber}%");
                }
                else
                {
                    // If it's not a numeric search, also search project number field with full term
                    searchGroup.AddCondition("isc_projectnumbernew", ConditionOperator.Like, $"%{searchTerm}%");
                }

                // Add search group to criteria
                query.Criteria.AddFilter(searchGroup);

                // Set page size for search results
                query.PageInfo = new PagingInfo
                {
                    Count = 200,
                    PageNumber = 1
                };

                var result = _connector._orgService.RetrieveMultiple(query);

                if (result?.Entities == null)
                {
                    return new List<Project>();
                }

                var entities = result.Entities.ToList();

                // Remove duplicates and filter out inactive/completed projects
                var uniqueProjects = entities
                    .GroupBy(e => e.Id)
                    .Select(g => g.First())
                    .Select(Project.FromEntity)
                    .Where(p => p != null && p.IsActive)
                    .ToList();

                // Debug logging
                System.Diagnostics.Debug.WriteLine($"Search for '{searchTerm}' returned {entities.Count} total entities, {uniqueProjects.Count} active projects");

                return uniqueProjects;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching projects: {ex.Message}",
                    "Search Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new List<Project>();
            }
        }

        // Remaining methods (CreateProject, UpdateProject, DeleteProject) remain largely the same
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