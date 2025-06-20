using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Sdk;
using DHA.DSTC.WPF.ProjectProperties;

namespace DHA.DSTC.WPF.Services
{
    public class DataverseAuthService
    {
        #region Private Fields
        private IOrganizationService _organizationService;
        private CrmServiceClient _client;
        private static DataverseAuthService _instance;
        private static readonly object _lock = new object();
        #endregion

        #region Configuration Properties
        // Use the application settings from ProjectProperties
        private string ClientId => Settings.Default.DataverseClientId;
        private string TenantId => Settings.Default.DataverseTenantId;
        private string EnvironmentUrl => Settings.Default.DataverseEnvironmentUrl;
        private string RedirectUri => "https://login.microsoftonline.com/common/oauth2/nativeclient";
        #endregion

        #region Public Properties
        public bool IsConnected => _organizationService != null && _client != null && _client.IsReady;
        public IOrganizationService OrganizationService => _organizationService;
        #endregion

        #region Singleton Pattern
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
        #endregion

        #region Public Methods
        public async Task<bool> ConnectAsync(bool forceReconnect = false)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== DataverseAuthService.ConnectAsync Starting ===");

                // Check if already connected
                if (IsConnected && !forceReconnect)
                {
                    System.Diagnostics.Debug.WriteLine("Already connected and not forcing reconnection");
                    return true;
                }

                // Clean up existing connection
                if (_client != null)
                {
                    System.Diagnostics.Debug.WriteLine("Disposing existing client");
                    try
                    {
                        _client.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning during client disposal: {disposeEx.Message}");
                    }
                    _client = null;
                    _organizationService = null;
                }

                // Validate configuration values
                System.Diagnostics.Debug.WriteLine($"ClientId: '{ClientId}'");
                System.Diagnostics.Debug.WriteLine($"TenantId: '{TenantId}'");
                System.Diagnostics.Debug.WriteLine($"EnvironmentUrl: '{EnvironmentUrl}'");
                System.Diagnostics.Debug.WriteLine($"RedirectUri: '{RedirectUri}'");

                if (string.IsNullOrEmpty(ClientId))
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: ClientId is null or empty");
                    return false;
                }

                if (string.IsNullOrEmpty(TenantId))
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: TenantId is null or empty");
                    return false;
                }

                if (string.IsNullOrEmpty(EnvironmentUrl))
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: EnvironmentUrl is null or empty");
                    return false;
                }

                // Clear any existing token cache to force fresh authentication
                await ClearTokenCacheAsync();

                // Build connection string - start with interactive login
                string connectionString = $"AuthType=OAuth;" +
                                        $"Url={EnvironmentUrl};" +
                                        $"AppId={ClientId};" +
                                        $"RedirectUri={RedirectUri};" +
                                        $"LoginPrompt=Always;" +
                                        $"RequireNewInstance=true";

                System.Diagnostics.Debug.WriteLine($"Connection string: {connectionString}");

                // Create the CRM Service Client on a background thread to avoid UI blocking
                System.Diagnostics.Debug.WriteLine("Creating CrmServiceClient...");

                bool clientCreated = await Task.Run(() =>
                {
                    try
                    {
                        _client = new CrmServiceClient(connectionString);
                        System.Diagnostics.Debug.WriteLine($"CrmServiceClient created. IsReady: {_client?.IsReady}");
                        return true;
                    }
                    catch (Exception clientEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Exception creating CrmServiceClient: {clientEx}");
                        System.Diagnostics.Debug.WriteLine($"Inner Exception: {clientEx.InnerException}");
                        return false;
                    }
                });

                if (!clientCreated)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to create CrmServiceClient");
                    return false;
                }

                // Check if client is ready
                if (_client == null)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: CrmServiceClient is null after creation");
                    return false;
                }

                if (!_client.IsReady)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: CrmServiceClient is not ready");
                    System.Diagnostics.Debug.WriteLine($"Last CRM Error: {_client.LastCrmError}");
                    System.Diagnostics.Debug.WriteLine($"Last CRM Exception: {_client.LastCrmException}");

                    // Clean up the failed client
                    try
                    {
                        _client.Dispose();
                    }
                    catch { }
                    _client = null;
                    return false;
                }

                // Get the organization service
                try
                {
                    _organizationService = _client.OrganizationServiceProxy ??
                                         (IOrganizationService)_client.OrganizationWebProxyClient ??
                                         _client;

                    if (_organizationService == null)
                    {
                        System.Diagnostics.Debug.WriteLine("ERROR: Could not obtain IOrganizationService");
                        return false;
                    }

                    System.Diagnostics.Debug.WriteLine("Successfully obtained IOrganizationService");
                }
                catch (Exception serviceEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Exception getting organization service: {serviceEx}");
                    return false;
                }

                // Test the connection with a simple operation
                try
                {
                    System.Diagnostics.Debug.WriteLine("Testing connection with WhoAmI request...");
                    var whoAmI = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                    var response = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)_organizationService.Execute(whoAmI);

                    System.Diagnostics.Debug.WriteLine($"WhoAmI successful! UserId: {response.UserId}");
                    System.Diagnostics.Debug.WriteLine($"Organization: {response.OrganizationId}");
                    System.Diagnostics.Debug.WriteLine("=== Connection Successful ===");

                    return true;
                }
                catch (Exception testEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Connection test failed: {testEx}");

                    // Clean up on test failure
                    _organizationService = null;
                    if (_client != null)
                    {
                        try
                        {
                            _client.Dispose();
                        }
                        catch { }
                        _client = null;
                    }
                    return false;
                }
            }
            catch (FileNotFoundException fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"FileNotFoundException in ConnectAsync: {fileEx}");
                System.Diagnostics.Debug.WriteLine($"Missing file: {fileEx.FileName}");
                System.Diagnostics.Debug.WriteLine($"Fusion log: {fileEx.FusionLog}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected exception in ConnectAsync: {ex}");
                System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Disconnecting DataverseAuthService...");

                _organizationService = null;

                if (_client != null)
                {
                    _client.Dispose();
                    _client = null;
                }

                System.Diagnostics.Debug.WriteLine("Disconnection completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during disconnect: {ex.Message}");
            }
        }
        #endregion

        #region Private Helper Methods
        private async Task ClearTokenCacheAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // Clear common token cache locations
                    string[] cachePaths = {
                        Path.Combine(Path.GetTempPath(), "DhaDataverseTokenCache"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                   "Microsoft", "PowerPlatform", "AuthCache"),
                        Path.Combine(Path.GetTempPath(), "tokencache")
                    };

                    foreach (string cachePath in cachePaths)
                    {
                        if (Directory.Exists(cachePath))
                        {
                            try
                            {
                                Directory.Delete(cachePath, true);
                                System.Diagnostics.Debug.WriteLine($"Cleared token cache: {cachePath}");
                            }
                            catch (Exception cacheEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Could not clear cache {cachePath}: {cacheEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error clearing token cache: {ex.Message}");
                }
            });
        }
        #endregion
    }
}