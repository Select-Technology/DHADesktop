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

        // Connection serialization - fix for double authentication
        private Task<bool> _connectTask;
        private readonly object _connectLock = new object();

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
            lock (_connectLock)
            {
                // If there's already a connection attempt in progress, return that task
                if (_connectTask != null && !_connectTask.IsCompleted)
                {
                    System.Diagnostics.Debug.WriteLine("ConnectAsync: Connection attempt already in progress, returning existing task");
                    return _connectTask.GetAwaiter().GetResult();
                }

                // Start a new connection attempt
                _connectTask = ConnectInternalAsync(forceReconnect);
            }

            return await _connectTask;
        }

        private async Task<bool> ConnectInternalAsync(bool forceReconnect = false)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ConnectAsync called: forceReconnect={forceReconnect}, IsConnected={IsConnected}");

                // Check if we already have a valid connection
                if (IsConnected && !forceReconnect && IsTokenStillValid())
                {
                    System.Diagnostics.Debug.WriteLine("Already connected with valid token, returning true");
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

                // Clear token cache only if explicitly forced to reconnect
                if (forceReconnect)
                {
                    System.Diagnostics.Debug.WriteLine("Force reconnect - clearing token cache for fresh login");
                    ClearTokenCache();
                }

                // Use longer timeout when debugging
                string timeout = System.Diagnostics.Debugger.IsAttached ? "00:05:00" : "00:02:00";

                // ✅ FIXED: Use consistent connection string that allows proper token reuse
                string connectionString = $"AuthType=OAuth;" +
                                         $"Url={EnvironmentUrl};" +
                                         $"AppId={ClientId};" +
                                         $"RedirectUri={RedirectUri};" +
                                         $"LoginPrompt=Auto;" +                    // ✅ Always use Auto - let MSAL decide
                                         $"TokenCacheStorePath={_tokenCacheDirectory};" +
                                         $"RequireNewInstance=false;" +            // ✅ Allow connection reuse
                                         $"Timeout={timeout}";

                System.Diagnostics.Debug.WriteLine($"Connection string: {connectionString}");
                System.Diagnostics.Debug.WriteLine($"Token cache path: {_tokenCacheDirectory}");

                // Create client
                System.Diagnostics.Debug.WriteLine("Creating CrmServiceClient...");
                _client = new CrmServiceClient(connectionString);

                // Wait longer for authentication to complete when debugging
                int maxWaitSeconds = System.Diagnostics.Debugger.IsAttached ? 300 : 60; // 5 minutes when debugging, 1 minute production
                int waitedSeconds = 0;

                System.Diagnostics.Debug.WriteLine($"Waiting for authentication (max {maxWaitSeconds} seconds)...");

                while (!_client.IsReady && waitedSeconds < maxWaitSeconds)
                {
                    await Task.Delay(1000);
                    waitedSeconds++;

                    // Log every second for the first 10 seconds when debugging
                    if (System.Diagnostics.Debugger.IsAttached && waitedSeconds <= 10)
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
                            System.Diagnostics.Debug.WriteLine($"CRM Exception Stack: {_client.LastCrmException.StackTrace}");
                        }
                    }
                    else if (waitedSeconds % 5 == 0) // Log every 5 seconds after that
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

                System.Diagnostics.Debug.WriteLine($"Connection string: {connectionString}");
                System.Diagnostics.Debug.WriteLine($"Token cache path: {_tokenCacheDirectory}");

                // Create client
                System.Diagnostics.Debug.WriteLine("Creating CrmServiceClient...");
                _client = new CrmServiceClient(connectionString);

                // Wait longer for authentication to complete when debugging
                maxWaitSeconds = System.Diagnostics.Debugger.IsAttached ? 300 : 60; // 5 minutes when debugging, 1 minute production
                waitedSeconds = 0;

                System.Diagnostics.Debug.WriteLine($"Waiting for authentication (max {maxWaitSeconds} seconds)...");

                while (!_client.IsReady && waitedSeconds < maxWaitSeconds)
                {
                    await Task.Delay(1000);
                    waitedSeconds++;

                    // Log every second for the first 10 seconds when debugging
                    if (System.Diagnostics.Debugger.IsAttached && waitedSeconds <= 10)
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
                            System.Diagnostics.Debug.WriteLine($"CRM Exception Stack: {_client.LastCrmException.StackTrace}");
                        }
                    }
                    else if (waitedSeconds % 5 == 0) // Log every 5 seconds after that
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

        /// <summary>
        /// ✅ NEW: Check if current token is still valid to avoid unnecessary reconnections
        /// </summary>
        private bool IsTokenStillValid()
        {
            try
            {
                if (_client?.IsReady == true && _organizationService != null)
                {
                    // Test with a quick operation to verify token is still valid
                    var whoAmI = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                    _organizationService.Execute(whoAmI);
                    System.Diagnostics.Debug.WriteLine("Token validation successful");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Token validation failed: {ex.Message}");
            }
            return false;
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