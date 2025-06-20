using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.DSTC.ViewModels
{
    /// <summary>
    /// View model for time entry operations
    /// </summary>
    public class TimeEntryViewModel
    {
        private readonly TimeEntryService _timeEntryService;
        private readonly ProjectService _projectService;
        private readonly TeamMemberService _teamMemberService;
        private readonly DataverseService _dataverseService;

        /// <summary>
        /// Initializes a new instance of the TimeEntryViewModel class
        /// </summary>
        public TimeEntryViewModel()
        {
            _timeEntryService = ServiceLocator.TimeEntryService;
            _projectService = ServiceLocator.ProjectService;
            _teamMemberService = ServiceLocator.TeamMemberService;
            _dataverseService = ServiceLocator.DataverseService;
        }

        /// <summary>
        /// Gets all time entries
        /// </summary>
        /// <returns>List of time entries</returns>
        public List<TimeEntry> GetTimeEntries()
        {
            return _timeEntryService.GetTimeEntries();
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
        /// Gets all team members
        /// </summary>
        /// <returns>List of team members</returns>
        public List<TeamMember> GetTeamMembers()
        {
            return _teamMemberService.GetTeamMembers();
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
        /// Saves a time entry
        /// </summary>
        /// <param name="timeEntry">Time entry to save</param>
        /// <returns>ID of the saved time entry, or null if operation failed</returns>
        public async Task<Guid?> SaveTimeEntryAsync(TimeEntry timeEntry)
        {
            try
            {
                if (_dataverseService != null)
                {
                    // Save using DataverseService
                    return await _dataverseService.SaveTimeEntryAsync(timeEntry);
                }
                else
                {
                    // Fall back to original method
                    return _timeEntryService.CreateTimeEntry(timeEntry);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Validates a time entry
        /// </summary>
        /// <param name="timeEntry">Time entry to validate</param>
        /// <param name="errorMessage">Error message if validation fails</param>
        /// <returns>True if valid, false otherwise</returns>
        public bool ValidateTimeEntry(TimeEntry timeEntry, out string errorMessage)
        {
            errorMessage = null;

            if (timeEntry.ProjectId == Guid.Empty)
            {
                errorMessage = "Please select a project.";
                return false;
            }

            if (timeEntry.TeamMemberId == Guid.Empty)
            {
                errorMessage = "Please select a team member.";
                return false;
            }

            if (timeEntry.Hours == 0 && timeEntry.Minutes == 0)
            {
                errorMessage = "Please enter time spent.";
                return false;
            }

            if (timeEntry.Date > DateTime.Today)
            {
                errorMessage = "Cannot enter time for future dates.";
                return false;
            }

            return true;
        }
    }
}