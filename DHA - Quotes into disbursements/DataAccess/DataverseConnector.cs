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
using DHA.DSTC.WPF.Utilities;

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
                // NOTE: We deliberately do NOT short-circuit here based on IsConnected alone.
                // ConnectAsync will call IsTokenStillValid() (cached for 5 min) to detect expired
                // OAuth tokens and reconnect silently via the cached refresh token.

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

        /// <summary>
        /// Executes a Dataverse operation. If it fails with an authentication / token-expiry
        /// error, the connection is silently refreshed using the cached MSAL refresh token
        /// and the operation is retried once before re-throwing.
        /// </summary>
        public T ExecuteWithRetry<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (Exception ex) when (IsAuthError(ex))
            {
                FileLogger.Warn($"Auth error detected, attempting silent token refresh. Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"DataverseConnector: Auth error — attempting silent reconnect. {ex.GetType().Name}: {ex.Message}");

                // Expire the in-memory validation cache so ConnectAsync actually re-validates.
                _authService.InvalidateTokenCache();

                bool reconnected = Connect();
                if (!reconnected)
                {
                    System.Diagnostics.Debug.WriteLine("DataverseConnector: Silent reconnect failed — re-throwing");
                    throw;
                }

                System.Diagnostics.Debug.WriteLine("DataverseConnector: Reconnected — retrying operation");
                return operation(); // retry once; if this also throws let it propagate
            }
        }

        private static bool IsAuthError(Exception ex)
        {
            if (ex == null) return false;

            // Specific exception types that indicate token/auth failures
            if (ex is System.ServiceModel.Security.MessageSecurityException) return true;
            if (ex is System.ServiceModel.Security.SecurityAccessDeniedException) return true;

            // Walk the inner-exception chain too
            var inner = ex.InnerException;
            while (inner != null)
            {
                if (inner is System.ServiceModel.Security.MessageSecurityException) return true;
                if (inner is System.ServiceModel.Security.SecurityAccessDeniedException) return true;
                inner = inner.InnerException;
            }

            // Fallback: inspect message text for well-known auth-failure keywords
            string msg = (ex.Message + " " + (ex.InnerException?.Message ?? "")).ToLowerInvariant();
            return msg.Contains("401") ||
                   msg.Contains("unauthorized") ||
                   (msg.Contains("token") && (msg.Contains("expir") || msg.Contains("invalid"))) ||
                   msg.Contains("authentication failed") ||
                   msg.Contains("accesstoken");
        }
    }
}