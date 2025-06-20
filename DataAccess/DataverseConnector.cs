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

                // Use true to force interactive login
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
                            userName = client.OAuthUserId ?? "Unknown";

                            // Double-check we have a valid connection
                            if (client.IsReady)
                            {
                                // Try a simple operation to verify connection
                                client.GetMyCrmUserId();
                            }
                            else
                            {
                                throw new Exception("CRM connection not ready: " + client.LastCrmError);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (showMessages)
                        {
                            MessageBox.Show($"Warning: Connected but couldn't verify user: {ex.Message}",
                                "Connection Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }

                    if (showMessages)
                    {
                        MessageBox.Show($"Successfully connected to Dataverse as: {userName}",
                            "Connection Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
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

        // Main RetrieveMultiple method - removed the ambiguous overloads
        public List<Entity> RetrieveMultiple(string entityName, string[] columns = null, string filterAttribute = null, object filterValue = null)
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

                // Add filter condition if provided
                if (!string.IsNullOrEmpty(filterAttribute) && filterValue != null)
                {
                    query.Criteria.AddCondition(new ConditionExpression(filterAttribute, ConditionOperator.Equal, filterValue));
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

        // Simplified overload for backwards compatibility
        public List<Entity> RetrieveMultiple(string entityName)
        {
            return RetrieveMultiple(entityName, null, null, null);
        }

        public Entity Retrieve(string entityName, Guid id, string[] columns = null)
        {
            try
            {
                if (_orgService == null)
                {
                    Connect();
                }

                return _orgService.Retrieve(entityName, id, columns != null ? new ColumnSet(columns) : new ColumnSet(true));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Retrieve: {ex.Message}");
                return null;
            }
        }

        public Guid Create(Entity entity)
        {
            try
            {
                if (_orgService == null)
                {
                    Connect();
                }

                return _orgService.Create(entity);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Create: {ex.Message}");
                throw; // Re-throw to let calling code handle
            }
        }

        public void Update(Entity entity)
        {
            try
            {
                if (_orgService == null)
                {
                    Connect();
                }

                _orgService.Update(entity);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Update: {ex.Message}");
                throw; // Re-throw to let calling code handle
            }
        }

        public void Delete(string entityName, Guid id)
        {
            try
            {
                if (_orgService == null)
                {
                    Connect();
                }

                _orgService.Delete(entityName, id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Delete: {ex.Message}");
                throw; // Re-throw to let calling code handle
            }
        }
    }
}