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
                // ✅ IMPROVED: Better connection state checking
                if (_authService.IsConnected && !forceReconnect)
                {
                    System.Diagnostics.Debug.WriteLine("DataverseConnector: Already connected, skipping authentication");
                    return true;
                }

                // Only show authentication message if specified AND it's a forced reconnect
                if (showMessages && forceReconnect)
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

                System.Diagnostics.Debug.WriteLine($"DataverseConnector: Calling ConnectAsync with forceReconnect={forceReconnect}");

                // ✅ FIXED: Pass forceReconnect parameter correctly
                bool result = _authService.ConnectAsync(forceReconnect).GetAwaiter().GetResult();

                System.Diagnostics.Debug.WriteLine($"DataverseConnector: ConnectAsync returned {result}");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DataverseConnector: Connection error: {ex.Message}");
                if (showMessages)
                {
                    MessageBox.Show($"Error connecting to Dataverse: {ex.Message}",
                        "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return false;
            }
        }

        // Main RetrieveMultiple method - removed the ambiguous overloads
        public List<Entity> RetrieveMultiple(string entityName, string[] columns = null, string filterAttribute = null, object filterValue = null, List<LinkEntity> links = null)
        {
            try
            {
                if (_orgService == null)
                {
                    System.Diagnostics.Debug.WriteLine("DataverseConnector: No organization service, attempting connection");
                    if (!Connect())
                    {
                        System.Diagnostics.Debug.WriteLine("DataverseConnector: Connection failed");
                        return new List<Entity>();
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

                // Add link-entity joins if provided
                if (links != null)
                {
                    foreach (var link in links)
                    {
                        query.LinkEntities.Add(link);
                    }
                }

                var result = _orgService.RetrieveMultiple(query);
                return result?.Entities?.ToList() ?? new List<Entity>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RetrieveMultiple: {ex.Message}");
                return new List<Entity>();
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
                    if (!Connect())
                    {
                        return null;
                    }
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
                    if (!Connect())
                    {
                        throw new InvalidOperationException("Could not establish connection to Dataverse");
                    }
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
                    if (!Connect())
                    {
                        throw new InvalidOperationException("Could not establish connection to Dataverse");
                    }
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
                    if (!Connect())
                    {
                        throw new InvalidOperationException("Could not establish connection to Dataverse");
                    }
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