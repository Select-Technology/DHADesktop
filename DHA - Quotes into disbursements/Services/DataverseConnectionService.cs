using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Collections.Generic;
using System.Linq;

namespace DHA.DSTC.WPF.Services
{
    public class DataverseConnectionService
    {
        private readonly DataverseAuthService _authService;

        public DataverseConnectionService()
        {
            _authService = DataverseAuthService.Instance;
        }

        public async Task<bool> ConnectAsync(bool forceReconnect = false)
        {
            return await _authService.ConnectAsync(forceReconnect);
        }

        public async Task<List<Entity>> RetrieveMultipleEntitiesAsync(string entityName, ColumnSet columns, string filterAttribute = null, object filterValue = null)
        {
            try
            {
                if (!_authService.IsConnected)
                {
                    if (!await ConnectAsync())
                        return null;
                }

                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columns
                };

                if (!string.IsNullOrEmpty(filterAttribute) && filterValue != null)
                {
                    query.Criteria.AddCondition(new ConditionExpression(
                        filterAttribute,
                        ConditionOperator.Equal,
                        filterValue));
                }

                var result = _authService.OrganizationService.RetrieveMultiple(query);

                return result.Entities.ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving data: {ex.Message}",
                    "Dataverse Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        public async Task<Entity> RetrieveEntityAsync(string entityName, Guid id, ColumnSet columns)
        {
            try
            {
                if (!_authService.IsConnected)
                {
                    if (!await ConnectAsync())
                        return null;
                }

                return _authService.OrganizationService.Retrieve(entityName, id, columns);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving entity: {ex.Message}",
                    "Dataverse Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        public async Task<Guid?> CreateEntityAsync(Entity entity)
        {
            try
            {
                if (!_authService.IsConnected)
                {
                    if (!await ConnectAsync())
                        return null;
                }

                return _authService.OrganizationService.Create(entity);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating entity: {ex.Message}",
                    "Dataverse Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        public async Task<bool> UpdateEntityAsync(Entity entity)
        {
            try
            {
                if (!_authService.IsConnected)
                {
                    if (!await ConnectAsync())
                        return false;
                }

                _authService.OrganizationService.Update(entity);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating entity: {ex.Message}",
                    "Dataverse Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public async Task<bool> DeleteEntityAsync(string entityName, Guid id)
        {
            try
            {
                if (!_authService.IsConnected)
                {
                    if (!await ConnectAsync())
                        return false;
                }

                _authService.OrganizationService.Delete(entityName, id);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting entity: {ex.Message}",
                    "Dataverse Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public void Disconnect()
        {
            _authService.Disconnect();
        }
    }
}