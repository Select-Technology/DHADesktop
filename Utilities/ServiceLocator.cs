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
        /// Initializes all services WITHOUT connecting
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public static bool Initialize()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ServiceLocator: Initialize() called");

                // Create connector but DO NOT connect yet
                DataverseConnector = new DataverseConnector();
                System.Diagnostics.Debug.WriteLine("ServiceLocator: DataverseConnector created");

                // Initialize services without connecting
                DataverseService = new DataverseService(DataverseConnector);
                TimeEntryService = new TimeEntryService(DataverseConnector);
                ProjectService = new ProjectService(DataverseConnector);
                TeamMemberService = new TeamMemberService(DataverseConnector);
                CalendarService = new CalendarService(TimeEntryService);
                DisbursementService = new DisbursementService(DataverseConnector);

                System.Diagnostics.Debug.WriteLine("ServiceLocator: All services created");

                // Don't get user info yet - that will happen after connection
                CurrentUserId = Guid.Empty;
                CurrentUserName = "Not connected";

                IsInitialized = true;
                System.Diagnostics.Debug.WriteLine("ServiceLocator: Initialize() completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                IsInitialized = false;
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Initialize() failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Connects to Dataverse and gets user info (call this AFTER Initialize)
        /// </summary>
        /// <param name="forceReconnect">Force fresh authentication</param>
        /// <param name="showMessages">Show authentication messages</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool Connect(bool forceReconnect = false, bool showMessages = false)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Connect() called - forceReconnect={forceReconnect}, showMessages={showMessages}");

                if (!IsInitialized)
                {
                    System.Diagnostics.Debug.WriteLine("ServiceLocator: Not initialized, calling Initialize() first");
                    if (!Initialize())
                    {
                        return false;
                    }
                }

                // Connect to Dataverse
                bool connected = DataverseConnector.Connect(forceReconnect, showMessages);
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: DataverseConnector.Connect() returned: {connected}");

                if (!connected)
                {
                    LastError = "Failed to connect to Dataverse";
                    System.Diagnostics.Debug.WriteLine("ServiceLocator: Connect() failed - returning false");
                    return false;
                }

                // Now get user information
                GetUserInfo();

                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Connect() completed successfully - returning true");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Connect() failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets current user information from the authentication service
        /// </summary>
        private static void GetUserInfo()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ServiceLocator: Getting user info...");

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
        }

        /// <summary>
        /// Disconnects all services
        /// </summary>
        public static void Disconnect()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ServiceLocator: Disconnect() called");

                // Disconnect the auth service instead of DataverseService
                var authService = Services.DataverseAuthService.Instance;
                authService?.Disconnect();

                // Clear current user info
                CurrentUserId = Guid.Empty;
                CurrentUserName = null;

                IsInitialized = false;
                System.Diagnostics.Debug.WriteLine("ServiceLocator: Disconnect() completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Disconnect error: {ex.Message}");
            }
        }
    }
}