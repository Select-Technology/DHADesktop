using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Services;
using Microsoft.Crm.Sdk.Messages;
using System;
using System.Configuration;
using System.Linq;

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
        public static QuoteService QuoteService { get; private set; } // NEW: Add quote service
        public static TeamMemberService TeamMemberService { get; private set; }
        public static CalendarService CalendarService { get; private set; }
        public static DisbursementService DisbursementService { get; private set; }
        public static ColleagueConfigurationService ColleagueConfigurationService { get; private set; }

        // Authentication info
        public static Guid CurrentUserId { get; private set; }
        public static string CurrentUserName { get; private set; }
        public static string CurrentUserEmail { get; private set; }

        // Impersonation support (admin only)
        public static bool IsImpersonating { get; private set; }
        public static Guid ImpersonatedUserId { get; private set; }
        public static string ImpersonatedUserName { get; private set; }

        /// <summary>
        /// Returns the effective user ID — the impersonated user if impersonating, otherwise the real user.
        /// Use this everywhere the app needs "who is the active user" for data queries.
        /// </summary>
        public static Guid EffectiveUserId => IsImpersonating ? ImpersonatedUserId : CurrentUserId;

        /// <summary>
        /// Returns the effective user display name.
        /// </summary>
        public static string EffectiveUserName => IsImpersonating ? ImpersonatedUserName : CurrentUserName;

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
                QuoteService = new QuoteService(DataverseConnector); // NEW: Initialize quote service
                TeamMemberService = new TeamMemberService(DataverseConnector);
                CalendarService = new CalendarService(TimeEntryService);
                DisbursementService = new DisbursementService(DataverseConnector);
                ColleagueConfigurationService = new ColleagueConfigurationService(DataverseConnector);

                System.Diagnostics.Debug.WriteLine("ServiceLocator: All services created including QuoteService");

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
        /// ✅ IMPROVED: Connects to Dataverse with better authentication handling
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
                    if (!Initialize())
                        return false;
                }

                var authService = Services.DataverseAuthService.Instance;

                // KEY FIX: More thorough connection state checking
                if (!forceReconnect && authService.IsConnected)
                {
                    // Verify the connection is actually working, not just that we have a client
                    try
                    {
                        var whoAmI = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                        authService.OrganizationService.Execute(whoAmI);
                        System.Diagnostics.Debug.WriteLine("ServiceLocator: Connection verified, skipping authentication");
                        GetUserInfo(); // Refresh user info
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ServiceLocator: Connection verification failed: {ex.Message}");
                        // Connection is stale, continue with authentication
                    }
                }

                // Connect to Dataverse - only show messages if explicitly requested
                bool connected = DataverseConnector.Connect(forceReconnect, showMessages);

                if (connected)
                {
                    GetUserInfo();
                }

                return connected;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Connect() failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ✅ IMPROVED: Gets current user information with better error handling
        /// </summary>
        private static void GetUserInfo()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ServiceLocator: Getting user info...");

                var authService = Services.DataverseAuthService.Instance;
                if (authService != null && authService.IsConnected)
                {
                    var client = authService.Client;

                    if (client != null && client.IsReady)
                    {
                        System.Diagnostics.Debug.WriteLine($"ServiceLocator: Client is ready, getting user info");

                        // Try multiple approaches to get user information

                        // Method 1: Try OAuth User ID first
                        try
                        {
                            CurrentUserName = client.OAuthUserId;
                            System.Diagnostics.Debug.WriteLine($"ServiceLocator: OAuthUserId: '{CurrentUserName}'");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"ServiceLocator: Error getting OAuthUserId: {ex.Message}");
                            CurrentUserName = null;
                        }

                        // Method 2: Try GetMyCrmUserId
                        try
                        {
                            CurrentUserId = client.GetMyCrmUserId();
                            System.Diagnostics.Debug.WriteLine($"ServiceLocator: GetMyCrmUserId returned: {CurrentUserId}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"ServiceLocator: Error calling GetMyCrmUserId: {ex.Message}");
                            CurrentUserId = Guid.Empty;
                        }

                        // Method 3: If GetMyCrmUserId failed, try WhoAmI request
                        if (CurrentUserId == Guid.Empty)
                        {
                            try
                            {
                                System.Diagnostics.Debug.WriteLine("ServiceLocator: GetMyCrmUserId returned empty, trying WhoAmI request");
                                var whoAmI = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                                var whoAmIResponse = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)authService.OrganizationService.Execute(whoAmI);
                                CurrentUserId = whoAmIResponse.UserId;
                                System.Diagnostics.Debug.WriteLine($"ServiceLocator: WhoAmI returned: {CurrentUserId}");
                            }
                            catch (Exception whoAmIEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"ServiceLocator: WhoAmI request failed: {whoAmIEx.Message}");
                                CurrentUserId = Guid.Empty;
                            }
                        }

                        // Method 4: If we still don't have a username, try to get it from the user record
                        if (string.IsNullOrEmpty(CurrentUserName) && CurrentUserId != Guid.Empty)
                        {
                            try
                            {
                                System.Diagnostics.Debug.WriteLine("ServiceLocator: Trying to get username from user record");
                                var userEntity = authService.OrganizationService.Retrieve("systemuser", CurrentUserId,
                                    new Microsoft.Xrm.Sdk.Query.ColumnSet("fullname", "domainname", "internalemailaddress"));

                                CurrentUserName = userEntity.GetAttributeValue<string>("domainname") ??
                                                 userEntity.GetAttributeValue<string>("internalemailaddress") ??
                                                 userEntity.GetAttributeValue<string>("fullname");

                                // Also capture email from user record if not already set
                                if (string.IsNullOrEmpty(CurrentUserEmail))
                                {
                                    CurrentUserEmail = (userEntity.GetAttributeValue<string>("internalemailaddress") ??
                                                       userEntity.GetAttributeValue<string>("domainname") ?? "").ToLower();
                                    System.Diagnostics.Debug.WriteLine($"ServiceLocator: Stored CurrentUserEmail from user record: '{CurrentUserEmail}'");
                                }

                                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Retrieved username from user record: '{CurrentUserName}'");
                            }
                            catch (Exception userEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Error getting username from user record: {userEx.Message}");
                            }
                        }

                        // Store the full email before cleanup
                        if (!string.IsNullOrEmpty(CurrentUserName) && CurrentUserName.Contains("@"))
                        {
                            CurrentUserEmail = CurrentUserName.ToLower();
                            System.Diagnostics.Debug.WriteLine($"ServiceLocator: Stored CurrentUserEmail: '{CurrentUserEmail}'");
                        }

                        // Clean up the user name if we got one
                        if (!string.IsNullOrEmpty(CurrentUserName))
                        {
                            // Extract just the username part if it's an email
                            if (CurrentUserName.Contains("@"))
                            {
                                CurrentUserName = CurrentUserName.Split('@')[0];
                            }

                            // Remove domain prefix if present (e.g., "DOMAIN\username" -> "username")
                            if (CurrentUserName.Contains("\\"))
                            {
                                CurrentUserName = CurrentUserName.Split('\\').Last();
                            }

                            System.Diagnostics.Debug.WriteLine($"ServiceLocator: Cleaned username: '{CurrentUserName}'");
                        }
                        else
                        {
                            CurrentUserName = "Unknown user";
                            System.Diagnostics.Debug.WriteLine("ServiceLocator: Could not determine username");
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
                    System.Diagnostics.Debug.WriteLine($"ServiceLocator: Auth service not available or not connected. AuthService null: {authService == null}, IsConnected: {authService?.IsConnected}");
                }
            }
            catch (Exception ex)
            {
                CurrentUserName = "Error getting user info: " + ex.Message;
                CurrentUserId = Guid.Empty;
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Exception getting user info: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Stack trace: {ex.StackTrace}");
            }

            // Log final result
            System.Diagnostics.Debug.WriteLine($"ServiceLocator: Final CurrentUserId: {CurrentUserId}, CurrentUserName: '{CurrentUserName}'");
        }


        /// <summary>
        /// Allowed emails that can use the impersonation feature.
        /// </summary>
        private static readonly string[] _impersonationAuthorisedEmails = new[]
        {
            "stsadmin@dhaplanning.co.uk"
        };

        /// <summary>
        /// Returns true if the currently authenticated user is allowed to impersonate others.
        /// </summary>
        public static bool CanImpersonate()
        {
            return !string.IsNullOrEmpty(CurrentUserEmail) &&
                   _impersonationAuthorisedEmails.Any(e => e.Equals(CurrentUserEmail, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Start impersonating another user. Sets CallerId on the Dataverse client so all
        /// subsequent queries run in that user's security context.
        /// </summary>
        public static bool StartImpersonation(Guid targetUserId, string targetUserName)
        {
            try
            {
                if (!CanImpersonate())
                {
                    System.Diagnostics.Debug.WriteLine("ServiceLocator: Impersonation denied — user not authorised");
                    return false;
                }

                var authService = Services.DataverseAuthService.Instance;
                authService.SetCallerId(targetUserId);

                ImpersonatedUserId = targetUserId;
                ImpersonatedUserName = targetUserName;
                IsImpersonating = true;

                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Now impersonating {targetUserName} ({targetUserId})");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Impersonation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stop impersonating and revert to the real authenticated user.
        /// </summary>
        public static void StopImpersonation()
        {
            try
            {
                var authService = Services.DataverseAuthService.Instance;
                authService.SetCallerId(Guid.Empty); // Clear CallerId

                IsImpersonating = false;
                ImpersonatedUserId = Guid.Empty;
                ImpersonatedUserName = null;

                System.Diagnostics.Debug.WriteLine("ServiceLocator: Impersonation stopped — reverted to real user");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Error stopping impersonation: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ IMPROVED: Better disconnect handling
        /// </summary>
        public static void Disconnect()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ServiceLocator: Disconnect() called");

                // Disconnect the auth service
                var authService = Services.DataverseAuthService.Instance;
                authService?.Disconnect();

                // Clear current user info
                CurrentUserId = Guid.Empty;
                CurrentUserName = "Disconnected";
                CurrentUserEmail = null;

                // Clear impersonation state
                IsImpersonating = false;
                ImpersonatedUserId = Guid.Empty;
                ImpersonatedUserName = null;

                // Don't mark as uninitialized - services are still available for reconnection
                System.Diagnostics.Debug.WriteLine("ServiceLocator: Disconnect() completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: Disconnect error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Force a fresh authentication (clears all cached tokens)
        /// </summary>
        public static bool ForceReauthentication(bool showMessages = true)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ServiceLocator: ForceReauthentication() called");

                // Clear cached tokens first
                var authService = Services.DataverseAuthService.Instance;
                authService?.ClearCachedTokens();

                // Force a fresh connection
                return Connect(forceReconnect: true, showMessages: showMessages);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                System.Diagnostics.Debug.WriteLine($"ServiceLocator: ForceReauthentication() failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ✅ NEW: Check if current connection is still valid
        /// </summary>
        public static bool IsConnectionValid()
        {
            try
            {
                var authService = Services.DataverseAuthService.Instance;
                return authService?.IsConnected == true;
            }
            catch
            {
                return false;
            }
        }
    }
}