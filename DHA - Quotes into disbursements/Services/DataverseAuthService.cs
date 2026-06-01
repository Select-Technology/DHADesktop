using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Sdk;
using DHA.DSTC.WPF.ProjectProperties;
using DHA.DSTC.WPF.Utilities;
using Microsoft.Xrm.Sdk.Client;
using System.Configuration;

namespace DHA.DSTC.WPF.Services
{
    public class DataverseAuthService
    {
        // Use Settings instead of hardcoded values - keep ProjectProperties namespace
        private string ClientId => "51f81489-12ee-4a9e-aaae-a2591f45987d"; // Microsoft's test app
        private string TenantId => ProjectProperties.Settings.Default.DataverseTenantId;
        private string _environmentUrlOverride;
        private string EnvironmentUrl => _environmentUrlOverride ?? ProjectProperties.Settings.Default.DataverseEnvironmentUrl;
        private string RedirectUri => "app://58145B91-0C36-4500-8554-080854F2AC97";

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

        // Cache for IsTokenStillValid — avoids a WhoAmI round-trip on every service call
        private DateTime _lastTokenValidation = DateTime.MinValue;
        private const double TokenValidationCacheSeconds = 300; // re-validate at most every 5 minutes

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
                FileLogger.Info($"ConnectAsync called: forceReconnect={forceReconnect}, IsConnected={IsConnected}");
                System.Diagnostics.Debug.WriteLine($"ConnectAsync called: forceReconnect={forceReconnect}, IsConnected={IsConnected}");

                // Better connection state validation
                if (!forceReconnect && IsConnected && IsTokenStillValid())
                {
                    System.Diagnostics.Debug.WriteLine("Using existing valid connection");
                    return true;
                }

                // Always dispose the stale client before reconnecting — even on natural token
                // expiry — to prevent zombie connections causing further 401 errors.
                if (_client != null)
                {
                    System.Diagnostics.Debug.WriteLine("Disposing existing CrmServiceClient before reconnect");
                    _client.Dispose();
                    _client = null;
                    _organizationService = null;
                }

                // Only wipe the token cache on an explicit forced reconnect.
                // On natural token expiry we keep the cache so the refresh token
                // (valid for ~90 days) allows a completely silent re-authentication.
                if (forceReconnect)
                {
                    System.Diagnostics.Debug.WriteLine("Force reconnect — clearing token cache");
                    ClearTokenCache();
                }

                // Create connection string with appropriate login prompt setting
                string timeout = System.Diagnostics.Debugger.IsAttached ? "00:05:00" : "00:02:00";
                string connectionString;

                // KEY FIX: Check if we have cached tokens before deciding login prompt behavior
                bool hasCachedTokens = HasCachedTokens();

                if (forceReconnect)
                {
                    // Force interactive login when explicitly requested
                    connectionString = $"AuthType=OAuth;" +
                                     $"Url={EnvironmentUrl};" +
                                     $"AppId={ClientId};" +
                                     $"RedirectUri={RedirectUri};" +
                                     $"LoginPrompt=Always;" +
                                     $"TokenCacheStorePath={_tokenCacheDirectory};" +
                                     $"RequireNewInstance=true;" +
                                     $"Timeout={timeout}";
                }
                else if (hasCachedTokens)
                {
                    // Prefer cached tokens but fall back to interactive login if they're expired.
                    // LoginPrompt=Auto lets ADAL try silently first, then prompt if needed.
                    // LoginPrompt=Never causes ADAL to probe every fallback auth path when tokens
                    // are stale, generating hundreds of internal InvalidCastExceptions and freezing
                    // the app during the CrmServiceClient constructor.
                    connectionString = $"AuthType=OAuth;" +
                                     $"Url={EnvironmentUrl};" +
                                     $"AppId={ClientId};" +
                                     $"RedirectUri={RedirectUri};" +
                                     $"LoginPrompt=Auto;" +
                                     $"TokenCacheStorePath={_tokenCacheDirectory};" +
                                     $"RequireNewInstance=false;" +
                                     $"Timeout={timeout}";
                }
                else
                {
                    // No cached tokens - allow interactive login
                    connectionString = $"AuthType=OAuth;" +
                                     $"Url={EnvironmentUrl};" +
                                     $"AppId={ClientId};" +
                                     $"RedirectUri={RedirectUri};" +
                                     $"LoginPrompt=Auto;" +  // Auto will prompt if needed
                                     $"TokenCacheStorePath={_tokenCacheDirectory};" +
                                     $"RequireNewInstance=false;" +
                                     $"Timeout={timeout}";
                }

                System.Diagnostics.Debug.WriteLine($"HasCachedTokens: {hasCachedTokens}");
                System.Diagnostics.Debug.WriteLine($"Connection string: {connectionString.Replace(ClientId, "***CLIENT_ID***")}");

                // Create client
                _client = new CrmServiceClient(connectionString);

                // Wait for authentication with timeout
                int maxWaitSeconds = System.Diagnostics.Debugger.IsAttached ? 300 : 60;
                int waitedSeconds = 0;

                while (!_client.IsReady && waitedSeconds < maxWaitSeconds)
                {
                    await Task.Delay(1000);
                    waitedSeconds++;

                    if (waitedSeconds % 10 == 0) // Log every 10 seconds
                    {
                        System.Diagnostics.Debug.WriteLine($"Waiting for authentication... ({waitedSeconds}s)");
                        System.Diagnostics.Debug.WriteLine($"Client IsReady: {_client.IsReady}");

                        if (!string.IsNullOrEmpty(_client.LastCrmError))
                        {
                            System.Diagnostics.Debug.WriteLine($"CRM Error: {_client.LastCrmError}");
                        }
                    }
                }

                if (!_client.IsReady)
                {
                    FileLogger.Error($"Connection failed after {waitedSeconds} seconds. LastCrmError={_client.LastCrmError}");
                    System.Diagnostics.Debug.WriteLine($"Connection failed after {waitedSeconds} seconds");
                    System.Diagnostics.Debug.WriteLine($"Last CRM Error: {_client.LastCrmError}");
                    System.Diagnostics.Debug.WriteLine($"Last CRM Exception: {_client.LastCrmException}");

                    _client?.Dispose();
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
                    FileLogger.Info($"Connected as UserId: {response.UserId}");
                    System.Diagnostics.Debug.WriteLine($"Connected as UserId: {response.UserId}");
                }
                catch (Exception userEx)
                {
                    FileLogger.Warn($"Could not verify connection: {userEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"Could not verify connection: {userEx.Message}");
                }

                _lastTokenValidation = DateTime.UtcNow; // seed the cache so WhoAmI isn't repeated immediately
                FileLogger.Info("Successfully connected to Dataverse");
                System.Diagnostics.Debug.WriteLine("Successfully connected to Dataverse");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Error("ConnectAsync FAILED", ex);
                System.Diagnostics.Debug.WriteLine($"Exception in ConnectAsync: {ex}");
                return false;
            }
        }

        private bool HasCachedTokens()
        {
            try
            {
                // Check if token cache directory exists and has files
                if (!Directory.Exists(_tokenCacheDirectory))
                {
                    System.Diagnostics.Debug.WriteLine("Token cache directory does not exist");
                    return false;
                }

                var cacheFiles = Directory.GetFiles(_tokenCacheDirectory, "*", SearchOption.AllDirectories);
                bool hasTokens = cacheFiles.Length > 0;

                System.Diagnostics.Debug.WriteLine($"Token cache directory: {_tokenCacheDirectory}");
                System.Diagnostics.Debug.WriteLine($"Cache files found: {cacheFiles.Length}");

                return hasTokens;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking cached tokens: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if the current token is still valid. Results are cached for 5 minutes to
        /// avoid an extra WhoAmI round-trip on every service call.
        /// </summary>
        private bool IsTokenStillValid()
        {
            try
            {
                if (_client?.IsReady == true && _organizationService != null)
                {
                    // Use cached result if the last successful validation was recent enough.
                    if ((DateTime.UtcNow - _lastTokenValidation).TotalSeconds < TokenValidationCacheSeconds)
                    {
                        System.Diagnostics.Debug.WriteLine("Token validation skipped — cached result is still fresh");
                        return true;
                    }

                    var whoAmI = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                    _organizationService.Execute(whoAmI);
                    _lastTokenValidation = DateTime.UtcNow;
                    System.Diagnostics.Debug.WriteLine("Token validation successful (WhoAmI)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Token validation failed: {ex.Message}");
                _lastTokenValidation = DateTime.MinValue; // force a real check next time
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

        /// <summary>
        /// Switch to a different Dataverse environment URL. Disconnects the current session.
        /// Call ConnectAsync after this to establish a new connection to the target environment.
        /// </summary>
        public void SwitchEnvironment(string newEnvironmentUrl)
        {
            System.Diagnostics.Debug.WriteLine($"DataverseAuthService: Switching environment to {newEnvironmentUrl}");
            Disconnect();
            ClearTokenCache();
            _environmentUrlOverride = newEnvironmentUrl;
        }

        /// <summary>
        /// Returns the current environment URL being used (override or default).
        /// </summary>
        public string GetCurrentEnvironmentUrl()
        {
            return EnvironmentUrl;
        }

        /// <summary>
        /// Set the CallerId on the CrmServiceClient to impersonate another user.
        /// All subsequent Dataverse requests will execute in that user's security context.
        /// Pass Guid.Empty to stop impersonating.
        /// Requires the calling user to have the prvActOnBehalfOfAnotherUser privilege.
        /// </summary>
        public void SetCallerId(Guid targetUserId)
        {
            if (_client == null || !_client.IsReady)
            {
                System.Diagnostics.Debug.WriteLine("DataverseAuthService: Cannot set CallerId - client not ready");
                return;
            }

            // Set CallerId on the CrmServiceClient
            _client.CallerId = targetUserId;
            System.Diagnostics.Debug.WriteLine($"DataverseAuthService: CallerId set to {targetUserId}");

            // CRITICAL FIX: The _organizationService may be an inner proxy object
            // (OrganizationServiceProxy or OrganizationWebProxyClient) that does NOT
            // reliably propagate CallerId. The CrmServiceClient itself implements
            // IOrganizationService and is the ONLY object guaranteed to apply CallerId
            // on every request. So when impersonating, redirect _organizationService
            // to _client directly.
            var previousType = _organizationService?.GetType().Name ?? "null";

            // Always route through CrmServiceClient — it handles CallerId for both
            // impersonating (targetUserId != Empty) and reverting (targetUserId == Empty).
            _organizationService = _client;

            System.Diagnostics.Debug.WriteLine($"DataverseAuthService: _organizationService switched from {previousType} to CrmServiceClient to ensure CallerId takes effect");
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

        /// <summary>
        /// Invalidates the in-memory token-validation cache so the next Connect() call
        /// performs a real WhoAmI round-trip and re-authenticates if the token has expired.
        /// Does NOT delete the on-disk token cache, so MSAL can still use the refresh token
        /// for a completely silent re-authentication.
        /// </summary>
        public void InvalidateTokenCache()
        {
            _lastTokenValidation = DateTime.MinValue;
            System.Diagnostics.Debug.WriteLine("DataverseAuthService: Token validation cache invalidated — next Connect() will re-validate");
        }
    }
}