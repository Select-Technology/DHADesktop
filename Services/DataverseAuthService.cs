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

        private DataverseAuthService() { }

        public async Task<bool> ConnectAsync(bool forceReconnect = false)
        {
            try
            {
                if (IsConnected && !forceReconnect)
                    return true;

                if (_client != null)
                {
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

                // Clear token cache completely to force fresh authentication
                if (forceReconnect)
                {
                    ClearAllTokenCaches();
                }

                // Use a simplified connection string that works better with interactive auth
                string connectionString;

                if (forceReconnect)
                {
                    // For forced reconnection, use a minimal connection string with Always prompt
                    connectionString = $"AuthType=OAuth;" +
                                     $"Url={EnvironmentUrl};" +
                                     $"AppId={ClientId};" +
                                     $"RedirectUri={RedirectUri};" +
                                     $"LoginPrompt=Always;" +
                                     $"RequireNewInstance=true";
                }
                else
                {
                    // For normal connection, try to use cached tokens
                    connectionString = $"AuthType=OAuth;" +
                                     $"Url={EnvironmentUrl};" +
                                     $"AppId={ClientId};" +
                                     $"RedirectUri={RedirectUri};" +
                                     $"LoginPrompt=Auto;" +
                                     $"TokenCacheStorePath=c:\\temp\\tokencache;" +
                                     $"RequireNewInstance=false";
                }

                System.Diagnostics.Debug.WriteLine($"Connection string: {connectionString}");

                // Ensure temp directory exists for token cache
                try
                {
                    if (!System.IO.Directory.Exists("c:\\temp"))
                    {
                        System.IO.Directory.CreateDirectory("c:\\temp");
                    }
                }
                catch { /* Ignore errors creating directory */ }

                // Create client
                _client = new CrmServiceClient(connectionString);

                // Wait a bit for authentication to complete
                int maxWaitSeconds = 60;
                int waitedSeconds = 0;

                while (!_client.IsReady && waitedSeconds < maxWaitSeconds)
                {
                    await Task.Delay(1000);
                    waitedSeconds++;

                    if (waitedSeconds % 5 == 0) // Log every 5 seconds
                    {
                        System.Diagnostics.Debug.WriteLine($"Waiting for authentication... ({waitedSeconds}s)");

                        if (!string.IsNullOrEmpty(_client.LastCrmError))
                        {
                            System.Diagnostics.Debug.WriteLine($"CRM Error: {_client.LastCrmError}");
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

        private void ClearAllTokenCaches()
        {
            try
            {
                // Clear multiple possible token cache locations
                string[] cachePaths = {
                    "c:\\temp\\tokencache",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "Microsoft", "PowerPlatform", "AuthCache"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "Microsoft", "MSAL.Desktop"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                               "Microsoft", "PowerPlatform")
                };

                foreach (string cachePath in cachePaths)
                {
                    if (System.IO.Directory.Exists(cachePath))
                    {
                        try
                        {
                            System.IO.Directory.Delete(cachePath, true);
                            System.Diagnostics.Debug.WriteLine($"Cleared token cache: {cachePath}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not clear cache {cachePath}: {ex.Message}");
                        }
                    }
                }

                // Also clear any MSAL cache files
                try
                {
                    string msalCacheFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                       "msal_token_cache.dat");
                    if (System.IO.File.Exists(msalCacheFile))
                    {
                        System.IO.File.Delete(msalCacheFile);
                        System.Diagnostics.Debug.WriteLine("Cleared MSAL token cache file");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not clear MSAL cache: {ex.Message}");
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
    }
}