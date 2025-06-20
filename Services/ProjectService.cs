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
                List<Entity> entities = _connector.RetrieveMultiple(_entityName);
                return entities.Select(Project.FromEntity).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving projects: {ex.Message}\nThe entity name used was: {_entityName}",
                    "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new List<Project>();
            }
        }

        public Project GetProject(Guid id)
        {
            try
            {
                Entity entity = _connector.Retrieve(_entityName, id);
                return Project.FromEntity(entity);
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
                _connector.Connect();

                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression(LogicalOperator.Or)
                };

                query.Criteria.AddCondition("fwp_name", ConditionOperator.Like, $"%{searchTerm}%");
                query.Criteria.AddCondition("fwp_number", ConditionOperator.Like, $"%{searchTerm}%");

                var entities = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
                return entities.Select(Project.FromEntity).ToList();
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