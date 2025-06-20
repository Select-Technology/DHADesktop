using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DHA.DSTC.WPF.Services;

namespace DHA.DSTC.WPF.DataAccess
{
    public class DataverseConnector
    {
        private readonly DataverseAuthService _authService;

        public IOrganizationService _orgService => _authService.OrganizationService;

        public DataverseConnector()
        {
            _authService = DataverseAuthService.Instance;
        }

        public bool Connect(bool forceReconnect = false, bool showMessages = false)
        {
            try
            {
                if (_authService.IsConnected && !forceReconnect)
                    return true;

                // Only show authentication message if specified
                if (showMessages)
                {
                    MessageBox.Show(
                        "The application will now attempt to connect to Dataverse.\n\n" +
                        "A login window will appear. Please complete the authentication process.",
                        "Authentication Required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    // Slight delay to allow UI to update before showing auth dialog
                    Thread.Sleep(500);
                }

                // FORCE INTERACTIVE LOGIN - This is the key difference!
                bool result = _authService.ConnectAsync(true).GetAwaiter().GetResult();

                if (result && showMessages)
                {
                    // Try to get current user info
                    string userName = "Unknown";
                    try
                    {
                        var client = _orgService as CrmServiceClient;
                        if (client != null)
                        {
                            userName = client.OAuthUserId ??
                                      client.ConnectedOrgFriendlyName ??
                                      "Connected User";
                        }
                    }
                    catch { }

                    MessageBox.Show($"Successfully connected to Dataverse!\n\nUser: {userName}",
                        "Connection Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return result;
            }
            catch (Exception ex)
            {
                if (showMessages)
                {
                    MessageBox.Show($"Error connecting to Dataverse: {ex.Message}",
                        "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return false;
            }
        }

        // RetrieveMultiple with required columns parameter
        public List<Entity> RetrieveMultiple(string entityName, string[] columns, string filterAttribute = null, object filterValue = null)
        {
            try
            {
                if (!_authService.IsConnected)
                    return null;

                var query = new QueryExpression(entityName)
                {
                    ColumnSet = new ColumnSet(columns)
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
                System.Diagnostics.Debug.WriteLine($"Error retrieving data: {ex.Message}");
                return null;
            }
        }

        // Overload for backward compatibility - calls RetrieveMultiple with all columns
        public List<Entity> RetrieveMultiple(string entityName, string[] columns = null, string filter = null)
        {
            try
            {
                if (_orgService == null)
                {
                    if (!Connect())
                    {
                        return new List<Entity>(); // Return empty list instead of null
                    }
                }

                QueryExpression query = new QueryExpression(entityName)
                {
                    ColumnSet = columns != null ? new ColumnSet(columns) : new ColumnSet(true)
                };

                if (!string.IsNullOrEmpty(filter))
                {
                    query.Criteria.AddCondition(new ConditionExpression(filter, ConditionOperator.Equal, true));
                }

                var result = _orgService.RetrieveMultiple(query);

                // Ensure we never return null - always return a list (even if empty)
                return result?.Entities?.ToList() ?? new List<Entity>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RetrieveMultiple: {ex.Message}");
                return new List<Entity>(); // Return empty list on error
            }
        }

        // Retrieve single entity with all columns (2-argument overload)
        public Entity Retrieve(string entityName, Guid id)
        {
            return Retrieve(entityName, id, new ColumnSet(true)); // true = all columns
        }

        // Retrieve single entity
        public Entity Retrieve(string entityName, Guid id, ColumnSet columns)
        {
            try
            {
                if (!_authService.IsConnected)
                    return null;

                return _authService.OrganizationService.Retrieve(entityName, id, columns);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error retrieving entity: {ex.Message}");
                return null;
            }
        }

        // Retrieve single entity with string array columns
        public Entity Retrieve(string entityName, Guid id, string[] columns)
        {
            return Retrieve(entityName, id, new ColumnSet(columns));
        }

        // Create entity
        public Guid Create(Entity entity)
        {
            try
            {
                if (!_authService.IsConnected)
                    return Guid.Empty;

                return _authService.OrganizationService.Create(entity);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating entity: {ex.Message}");
                return Guid.Empty;
            }
        }

        // Update entity
        public void Update(Entity entity)
        {
            try
            {
                if (!_authService.IsConnected)
                    return;

                _authService.OrganizationService.Update(entity);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating entity: {ex.Message}");
            }
        }

        // Delete entity
        public void Delete(string entityName, Guid id)
        {
            try
            {
                if (!_authService.IsConnected)
                    return;

                _authService.OrganizationService.Delete(entityName, id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting entity: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            _authService.Disconnect();
        }
    }
}