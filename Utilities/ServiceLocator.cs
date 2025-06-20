using System;
using System.Configuration;
using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Services;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Tooling.Connector;

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

                // Try to get user information from the auth service directly - SIMPLIFIED ACCESS
                try
                {
                    var authService = Services.DataverseAuthService.Instance;
                    if (authService != null && authService.IsConnected)
                    {
                        var client = authService.Client; // Use public property instead of reflection

                        if (client != null && client.IsReady)
                        {
                            // Try multiple methods to get the username - SAME AS WORKING PROJECT
                            CurrentUserName = client.OAuthUserId ??
                                            client.ConnectedOrgFriendlyName ??
                                            "Unknown user";
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
                                    var whoAmI = new WhoAmIRequest();
                                    var whoAmIResponse = (WhoAmIResponse)client.Execute(whoAmI);
                                    CurrentUserId = whoAmIResponse.UserId;
                                    System.Diagnostics.Debug.WriteLine($"ServiceLocator: WhoAmI returned: {CurrentUserId}");

                                    // Try to get a better username using the systemuser entity
                                    if (CurrentUserId != Guid.Empty && string.IsNullOrEmpty(client.OAuthUserId))
                                    {
                                        try
                                        {
                                            var userEntity = client.Retrieve("systemuser", CurrentUserId,
                                                new Microsoft.Xrm.Sdk.Query.ColumnSet("fullname", "domainname"));

                                            if (userEntity != null)
                                            {
                                                string fullName = userEntity.GetAttributeValue<string>("fullname");
                                                string domainName = userEntity.GetAttributeValue<string>("domainname");

                                                CurrentUserName = fullName ?? domainName ?? CurrentUserName;
                                                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Retrieved user name from systemuser: {CurrentUserName}");
                                            }
                                        }
                                        catch (Exception userQueryEx)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"ServiceLocator: Could not query systemuser: {userQueryEx.Message}");
                                        }
                                    }
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
                            System.Diagnostics.Debug.WriteLine($"ServiceLocator: Client issues - IsReady: {client?.IsReady}");
                        }
                    }
                    else
                    {
                        CurrentUserName = "Auth service not available";
                        CurrentUserId = Guid.Empty;
                        System.Diagnostics.Debug.WriteLine("ServiceLocator: Auth service not connected");
                    }
                }
                catch (Exception authEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ServiceLocator: Error getting auth info: {authEx.Message}");
                    CurrentUserName = "Error retrieving user";
                    CurrentUserId = Guid.Empty;
                }

                // Initialize essential services only
                try
                {
                    TimeEntryService = new TimeEntryService(DataverseConnector);
                    System.Diagnostics.Debug.WriteLine("TimeEntryService created successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not create TimeEntryService: {ex.Message}");
                    TimeEntryService = null;
                }

                try
                {
                    ProjectService = new ProjectService(DataverseConnector);
                    System.Diagnostics.Debug.WriteLine("ProjectService created successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not create ProjectService: {ex.Message}");
                    ProjectService = null;
                }

                try
                {
                    TeamMemberService = new TeamMemberService(DataverseConnector);
                    System.Diagnostics.Debug.WriteLine("TeamMemberService created successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not create TeamMemberService: {ex.Message}");
                    TeamMemberService = null;
                }

                // Try to create other services if they exist
                try
                {
                    DataverseService = new DataverseService(DataverseConnector);
                    System.Diagnostics.Debug.WriteLine("DataverseService created successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"DataverseService not available: {ex.Message}");
                    DataverseService = null;
                }

                // Try to create CalendarService (requires TimeEntryService)
                try
                {
                    if (TimeEntryService != null)
                    {
                        CalendarService = new CalendarService(TimeEntryService);
                        System.Diagnostics.Debug.WriteLine("CalendarService created successfully");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("CalendarService not created - TimeEntryService is null");
                        CalendarService = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CalendarService not available: {ex.Message}");
                    CalendarService = null;
                }

                try
                {
                    DisbursementService = new DisbursementService(DataverseConnector);
                    System.Diagnostics.Debug.WriteLine("DisbursementService created successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"DisbursementService not available: {ex.Message}");
                    DisbursementService = null;
                }

                IsInitialized = true;
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Initialization complete. User: {CurrentUserName}, UserId: {CurrentUserId}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cleans up all services
        /// </summary>
        public static void Cleanup()
        {
            try
            {
                DataverseConnector?.Disconnect();

                // Clear references
                DataverseConnector = null;
                DataverseService = null;
                TimeEntryService = null;
                ProjectService = null;
                TeamMemberService = null;
                CalendarService = null;
                DisbursementService = null;

                // Clear user info
                CurrentUserId = Guid.Empty;
                CurrentUserName = null;

                IsInitialized = false;
                System.Diagnostics.Debug.WriteLine("ServiceLocator: Cleanup complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Error during cleanup: {ex.Message}");
            }
        }
    }
}