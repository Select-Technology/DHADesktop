using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Sdk;
using DHA.DSTC.WPF.ProjectProperties;
using Microsoft.Xrm.Sdk.Client;
using System.Configuration;

namespace DHA.DSTC.WPF.Services
{
    public class DataverseAuthService
    {
        // Use Settings instead of hardcoded values - keep ProjectProperties namespace
        private string ClientId => ProjectProperties.Settings.Default.DataverseClientId;
        private string TenantId => ProjectProperties.Settings.Default.DataverseTenantId;
        private string EnvironmentUrl => ProjectProperties.Settings.Default.DataverseEnvironmentUrl;
        private string RedirectUri => "https://login.microsoftonline.com/common/oauth2/nativeclient";

        private IOrganizationService _organizationService;
        private CrmServiceClient _client;
        private static DataverseAuthService _instance;
        private static readonly object _lock = new object();

        // Token cache settings
        private readonly string _tokenCacheDirectory;
        private readonly string _tokenCacheFile;

        public bool IsConnected => _organizationService != null && _client != null && _client.IsReady;

        public IOrganizationService OrganizationService => _organizationService;

        // Add public access to the client for user identification
        public CrmServiceClient Client => _client;

        // Singleton pattern
        public static DataverseAuthService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DataverseAuthService();
                        }
                    }
                }
                return _instance;
            }
        }

        private DataverseAuthService()
        {
            // Set up persistent token cache location
            _tokenCacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DHA", "TimeManagement", "TokenCache");

            _tokenCacheFile = Path.Combine(_tokenCacheDirectory, "msal_cache.dat");

            // Ensure directory exists
            try
            {
                if (!Directory.Exists(_tokenCacheDirectory))
                {
                    Directory.CreateDirectory(_tokenCacheDirectory);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not create token cache directory: {ex.Message}");
            }
        }

        public async Task<bool> ConnectAsync(bool forceReconnect = false)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ConnectAsync called: forceReconnect={forceReconnect}, IsConnected={IsConnected}");

                if (IsConnected && !forceReconnect)
                {
                    System.Diagnostics.Debug.WriteLine("Already connected, returning true");
                    return true;
                }

                if (_client != null)
                {
                    System.Diagnostics.Debug.WriteLine("Disposing existing client");
                    _client.Dispose();
                    _client = null;
                    _organizationService = null;
                }

                System.Diagnostics.Debug.WriteLine($"ClientId: '{ClientId}'");
                System.Diagnostics.Debug.WriteLine($"TenantId: '{TenantId}'");
                System.Diagnostics.Debug.WriteLine($"EnvironmentUrl: '{EnvironmentUrl}'");

                if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(TenantId) || string.IsNullOrEmpty(EnvironmentUrl))
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: One or more settings are null/empty");
                    return false;
                }

                // Clear token cache if explicitly forced to reconnect OR if running from debugger
                if (forceReconnect || System.Diagnostics.Debugger.IsAttached)
                {
                    System.Diagnostics.Debug.WriteLine("Clearing token cache for fresh login");
                    ClearTokenCache();
                }

                // Use different connection strings for debugging vs production
                string connectionString;

                if (System.Diagnostics.Debugger.IsAttached)
                {
                    // When debugging, always prompt for login and increase timeout
                    connectionString = $"AuthType=OAuth;" +
                                     $"Url={EnvironmentUrl};" +
                                     $"AppId={ClientId};" +
                                     $"RedirectUri={RedirectUri};" +
                                     $"LoginPrompt=Always;" +
                                     $"TokenCacheStorePath={_tokenCacheDirectory};" +
                                     $"RequireNewInstance=true;" +
                                     $"Timeout=00:05:00"; // 5 minute timeout for debugging
                }
                else if (forceReconnect)
                {
                    // For forced reconnection in production, always prompt for login
                    connectionString = $"AuthType=OAuth;" +
                                     $"Url={EnvironmentUrl};" +
                                     $"AppId={ClientId};" +
                                     $"RedirectUri={RedirectUri};" +
                                     $"LoginPrompt=Always;" +
                                     $"TokenCacheStorePath={_tokenCacheDirectory};" +
                                     $"RequireNewInstance=true;" +
                                     $"Timeout=00:02:00"; // 2 minute timeout
                }
                else
                {
                    // For normal connection, try to use cached tokens with Auto prompt
                    connectionString = $"AuthType=OAuth;" +
                                     $"Url={EnvironmentUrl};" +
                                     $"AppId={ClientId};" +
                                     $"RedirectUri={RedirectUri};" +
                                     $"LoginPrompt=Auto;" +
                                     $"TokenCacheStorePath={_tokenCacheDirectory};" +
                                     $"RequireNewInstance=false;" +
                                     $"Timeout=00:02:00"; // 2 minute timeout
                }

                System.Diagnostics.Debug.WriteLine($"Connection string: {connectionString}");
                System.Diagnostics.Debug.WriteLine($"Token cache path: {_tokenCacheDirectory}");

                // Create client
                System.Diagnostics.Debug.WriteLine("Creating CrmServiceClient...");
                _client = new CrmServiceClient(connectionString);

                // Wait longer for authentication to complete when debugging
                int maxWaitSeconds = System.Diagnostics.Debugger.IsAttached ? 120 : 60; // 2 minutes when debugging
                int waitedSeconds = 0;

                System.Diagnostics.Debug.WriteLine($"Waiting for authentication (max {maxWaitSeconds} seconds)...");

                while (!_client.IsReady && waitedSeconds < maxWaitSeconds)
                {
                    await Task.Delay(1000);
                    waitedSeconds++;

                    if (waitedSeconds % 5 == 0) // Log every 5 seconds
                    {
                        System.Diagnostics.Debug.WriteLine($"Waiting for authentication... ({waitedSeconds}s)");
                        System.Diagnostics.Debug.WriteLine($"Client IsReady: {_client.IsReady}");

                        if (!string.IsNullOrEmpty(_client.LastCrmError))
                        {
                            System.Diagnostics.Debug.WriteLine($"CRM Error: {_client.LastCrmError}");
                        }

                        if (_client.LastCrmException != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"CRM Exception: {_client.LastCrmException.Message}");
                        }
                    }
                }

                if (!_client.IsReady)
                {
                    System.Diagnostics.Debug.WriteLine($"Connection failed after {waitedSeconds} seconds");
                    System.Diagnostics.Debug.WriteLine($"Last CRM Error: {_client.LastCrmError}");
                    System.Diagnostics.Debug.WriteLine($"Last CRM Exception: {_client.LastCrmException}");

                    // Clean up failed client
                    _client.Dispose();
                    _client = null;
                    return false;
                }

                _organizationService = _client.OrganizationServiceProxy ??
                                      (IOrganizationService)_client.OrganizationWebProxyClient ??
                                      _client;

                // Test the connection
                try
                {
                    var whoAmI = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                    var response = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)_organizationService.Execute(whoAmI);
                    System.Diagnostics.Debug.WriteLine($"Connected as UserId: {response.UserId}");
                    System.Diagnostics.Debug.WriteLine($"OAuthUserId: {_client.OAuthUserId}");
                    System.Diagnostics.Debug.WriteLine($"ConnectedOrgFriendlyName: {_client.ConnectedOrgFriendlyName}");
                }
                catch (Exception userEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not get user info: {userEx.Message}");
                }

                System.Diagnostics.Debug.WriteLine("Successfully connected to Dataverse");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in ConnectAsync: {ex}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private void ClearTokenCache()
        {
            try
            {
                // Clear the specific token cache directory we're using
                if (Directory.Exists(_tokenCacheDirectory))
                {
                    try
                    {
                        Directory.Delete(_tokenCacheDirectory, true);
                        Directory.CreateDirectory(_tokenCacheDirectory); // Recreate empty directory
                        System.Diagnostics.Debug.WriteLine($"Cleared token cache: {_tokenCacheDirectory}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not clear cache {_tokenCacheDirectory}: {ex.Message}");
                    }
                }

                // Also clear any other common cache locations as backup
                string[] additionalCachePaths = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "Microsoft", "PowerPlatform", "AuthCache"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "Microsoft", "MSAL.Desktop"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                               "Microsoft", "PowerPlatform")
                };

                foreach (string cachePath in additionalCachePaths)
                {
                    if (Directory.Exists(cachePath))
                    {
                        try
                        {
                            Directory.Delete(cachePath, true);
                            System.Diagnostics.Debug.WriteLine($"Cleared additional cache: {cachePath}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not clear cache {cachePath}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing token cache: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            _organizationService = null;
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
        }

        /// <summary>
        /// Manually clear cached tokens (for troubleshooting or logout)
        /// </summary>
        public void ClearCachedTokens()
        {
            ClearTokenCache();
        }
    }
}