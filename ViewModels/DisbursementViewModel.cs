using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DHA.DSTC.Models;
using DHA.DSTC.Services;
using DHA.DSTC.Utilities;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.DSTC.ViewModels
{
    /// <summary>
    /// View model for disbursement operations
    /// </summary>
    public class DisbursementViewModel
    {
        private readonly DisbursementService _disbursementService;
        private readonly ProjectService _projectService;
        private readonly TeamMemberService _teamMemberService;
        private readonly DataverseService _dataverseService;

        /// <summary>
        /// Initializes a new instance of the DisbursementViewModel class
        /// </summary>
        public DisbursementViewModel()
        {
            _disbursementService = ServiceLocator.DisbursementService;
            _projectService = ServiceLocator.ProjectService;
            _teamMemberService = ServiceLocator.TeamMemberService;
            _dataverseService = ServiceLocator.DataverseService;
        }

        /// <summary>
        /// Gets all disbursement types
        /// </summary>
        /// <returns>List of disbursement types</returns>
        public async Task<List<DisbursementType>> GetDisbursementTypesAsync()
        {
            return await _disbursementService.GetAllDisbursementTypesAsync();
        }

        /// <summary>
        /// Gets all team members
        /// </summary>
        /// <returns>List of team members</returns>
        public List<TeamMember> GetTeamMembers()
        {
            return _teamMemberService.GetTeamMembers();
        }

        /// <summary>
        /// Gets all projects
        /// </summary>
        /// <returns>List of projects</returns>
        public List<Project> GetProjects()
        {
            return _projectService.GetProjects();
        }

        /// <summary>
        /// Searches for projects matching the search term
        /// </summary>
        /// <param name="searchTerm">Search term</param>
        /// <returns>List of matching projects</returns>
        public async Task<List<Project>> SearchProjectsAsync(string searchTerm)
        {
            try
            {
                if (_dataverseService != null)
                {
                    // Create a filter expression for the search
                    var filter = new FilterExpression(LogicalOperator.Or);

                    if (!string.IsNullOrEmpty(searchTerm))
                    {
                        filter.AddCondition("msdyn_subject", ConditionOperator.Like, $"%{searchTerm}%");
                        filter.AddCondition("isc_projectnumbernew", ConditionOperator.Like, $"%{searchTerm}%");
                    }

                    var projects = await _dataverseService.RetrieveMultipleEntitiesAsync(
                        "msdyn_project",
                        new ColumnSet("msdyn_subject", "isc_projectnumbernew", "msdyn_customer"),
                        filter);

                    if (projects != null)
                    {
                        return projects.Select(Project.FromEntity).ToList();
                    }

                    return new List<Project>();
                }
                else
                {
                    // Fall back to original method if service not available
                    return _projectService.SearchProjects(searchTerm);
                }
            }
            catch (Exception)
            {
                // Return empty list on error - UI can handle error display
                return new List<Project>();
            }
        }

        /// <summary>
        /// Gets disbursements for a project
        /// </summary>
        /// <param name="projectId">Project ID</param>
        /// <returns>List of disbursements</returns>
        public async Task<List<Disbursement>> GetDisbursementsByProjectAsync(Guid projectId)
        {
            try
            {
                // Now using the Guid directly since we're working with Dataverse
                return await _disbursementService.GetDisbursementsByProjectAsync(projectId);
            }
            catch (Exception)
            {
                return new List<Disbursement>();
            }
        }

        /// <summary>
        /// Saves a disbursement
        /// </summary>
        /// <param name="disbursement">Disbursement to save</param>
        /// <returns>ID of the saved disbursement, or -1 if the operation failed</returns>
        public async Task<int> SaveDisbursementAsync(Disbursement disbursement)
        {
            try
            {
                if (disbursement.Id == 0)
                {
                    // Add new disbursement
                    Guid newId = await _disbursementService.AddDisbursementAsync(disbursement);

                    // Convert Guid to int for compatibility with existing code
                    return newId != Guid.Empty ? GuidConverter.GetDeterministicIntId(newId) : -1;
                }
                else
                {
                    // Update existing disbursement
                    await _disbursementService.UpdateDisbursementAsync(disbursement);
                    return disbursement.Id;
                }
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// Validates a disbursement
        /// </summary>
        /// <param name="disbursement">Disbursement to validate</param>
        /// <param name="errorMessage">Error message if validation fails</param>
        /// <returns>True if valid, false otherwise</returns>
        public bool ValidateDisbursement(Disbursement disbursement, out string errorMessage)
        {
            errorMessage = null;

            if (disbursement.ProjectId <= 0)
            {
                errorMessage = "Please select a project.";
                return false;
            }

            if (disbursement.TeamMemberId <= 0)
            {
                errorMessage = "Please select a team member.";
                return false;
            }

            if (disbursement.DisbursementTypeId <= 0)
            {
                errorMessage = "Please select a disbursement type.";
                return false;
            }

            if (disbursement.Amount <= 0)
            {
                errorMessage = "Please enter a valid amount.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(disbursement.Description))
            {
                errorMessage = "Please enter a description.";
                return false;
            }

            if (disbursement.Date > DateTime.Today)
            {
                errorMessage = "Cannot enter disbursements for future dates.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates a new disbursement instance with default values
        /// </summary>
        /// <returns>New disbursement with defaults</returns>
        public Disbursement CreateNewDisbursement()
        {
            return new Disbursement
            {
                Date = DateTime.Today,
                BillableToClient = true
            };
        }

        /// <summary>
        /// Updates an existing disbursement
        /// </summary>
        /// <param name="disbursement">Disbursement to update</param>
        /// <returns>True if update was successful, false otherwise</returns>
        public async Task UpdateDisbursementAsync(Disbursement disbursement)
        {
            try
            {
                await _disbursementService.UpdateDisbursementAsync(disbursement);
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating disbursement: {ex.Message}");
                throw; // Let the UI handle the exception
            }
        }

        /// <summary>
        /// Adds a new disbursement
        /// </summary>
        /// <param name="disbursement">Disbursement to add</param>
        /// <returns>ID of the new disbursement, or empty GUID if operation failed</returns>
        public async Task<Guid> AddDisbursementAsync(Disbursement disbursement)
        {
            try
            {
                return await _disbursementService.AddDisbursementAsync(disbursement);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding disbursement: {ex.Message}");
                throw; // Let the UI handle the exception
            }
        }
    }
}