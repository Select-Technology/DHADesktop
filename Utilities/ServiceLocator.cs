using System;
using System.Configuration;
using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Services;
using Microsoft.Crm.Sdk.Messages;

namespace DHA.DSTC.WPF.Utilities
{
    /// <summary>
    /// Central access point for all services in the application
    /// </summary>
    public static class ServiceLocator
    {
        // Core connectivity
        public static DataverseConnector DataverseConnector { get; private set; }
        public static DataverseService DataverseService { get; private set; }

        // Service instances
        public static TimeEntryService TimeEntryService { get; private set; }
        public static ProjectService ProjectService { get; private set; }
        public static TeamMemberService TeamMemberService { get; private set; }
        public static CalendarService CalendarService { get; private set; }
        public static DisbursementService DisbursementService { get; private set; }

        // Authentication info
        public static Guid CurrentUserId { get; private set; }
        public static string CurrentUserName { get; private set; }

        // Status
        public static bool IsInitialized { get; private set; }
        public static string LastError { get; private set; }

        /// <summary>
        /// Initializes all services
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public static bool Initialize()
        {
            try
            {
                // Create connector
                DataverseConnector = new DataverseConnector();
                bool connected = DataverseConnector.Connect(showMessages: true);

                if (!connected)
                {
                    LastError = "Failed to connect to Dataverse";
                    return false;
                }

                // Try to get user information from the auth service directly
                try
                {
                    var authService = Services.DataverseAuthService.Instance;
                    if (authService != null && authService.IsConnected)
                    {
                        // Access the internal client through reflection or add a public property
                        var clientField = typeof(Services.DataverseAuthService).GetField("_client",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        var client = clientField?.GetValue(authService) as Microsoft.Xrm.Tooling.Connector.CrmServiceClient;

                        if (client != null && client.IsReady)
                        {
                            CurrentUserName = client.OAuthUserId ?? "Unknown user";
                            System.Diagnostics.Debug.WriteLine($"ServiceLocator: Found client, OAuthUserId: {CurrentUserName}");

                            // Try multiple methods to get the current user ID
                            try
                            {
                                CurrentUserId = client.GetMyCrmUserId();
                                System.Diagnostics.Debug.WriteLine($"ServiceLocator: GetMyCrmUserId returned: {CurrentUserId}");

                                // If that returns empty, try using WhoAmI request
                                if (CurrentUserId == Guid.Empty)
                                {
                                    System.Diagnostics.Debug.WriteLine("ServiceLocator: GetMyCrmUserId returned empty, trying WhoAmI request");
                                    var whoAmI = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                                    var whoAmIResponse = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)client.Execute(whoAmI);
                                    CurrentUserId = whoAmIResponse.UserId;
                                    System.Diagnostics.Debug.WriteLine($"ServiceLocator: WhoAmI returned: {CurrentUserId}");
                                }
                            }
                            catch (Exception userEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Error getting user ID: {userEx.Message}");
                                CurrentUserId = Guid.Empty;
                            }
                        }
                        else
                        {
                            CurrentUserName = client?.IsReady == false ? "Client not ready" : "No client available";
                            CurrentUserId = Guid.Empty;
                            System.Diagnostics.Debug.WriteLine($"ServiceLocator: Client issues. Client null: {client == null}, IsReady: {client?.IsReady}");
                        }
                    }
                    else
                    {
                        CurrentUserName = "Auth service not connected";
                        CurrentUserId = Guid.Empty;
                        System.Diagnostics.Debug.WriteLine($"ServiceLocator: Auth service not available or not connected");
                    }
                }
                catch (Exception ex)
                {
                    CurrentUserName = "Error getting user info: " + ex.Message;
                    CurrentUserId = Guid.Empty;
                    System.Diagnostics.Debug.WriteLine($"ServiceLocator: Exception getting user info: {ex.Message}");
                }

                // Log final result
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Final CurrentUserId: {CurrentUserId}, CurrentUserName: {CurrentUserName}");

                // Initialize services
                DataverseService = new DataverseService(DataverseConnector);
                TimeEntryService = new TimeEntryService(DataverseConnector);
                ProjectService = new ProjectService(DataverseConnector);
                TeamMemberService = new TeamMemberService(DataverseConnector);
                CalendarService = new CalendarService(TimeEntryService);

                // Initialize disbursement service with Dataverse connector
                DisbursementService = new DisbursementService(DataverseConnector);

                IsInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                IsInitialized = false;
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Initialize failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disconnects all services
        /// </summary>
        public static void Disconnect()
        {
            try
            {
                // Disconnect the auth service instead of DataverseService
                var authService = Services.DataverseAuthService.Instance;
                authService?.Disconnect();

                // Clear current user info
                CurrentUserId = Guid.Empty;
                CurrentUserName = null;

                IsInitialized = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Disconnect error: {ex.Message}");
            }
        }
    }
}