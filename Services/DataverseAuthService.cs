using System;
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
        // Use Settings instead of hardcoded values
        // Use the application settings (not user settings)
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

                // Build connection string with more explicit OAuth parameters
                string connectionString = $"AuthType=OAuth;Url={EnvironmentUrl};AppId={ClientId};RedirectUri={RedirectUri};LoginPrompt=Never";

                System.Diagnostics.Debug.WriteLine($"ClientId: '{ClientId}'");
                System.Diagnostics.Debug.WriteLine($"TenantId: '{TenantId}'");
                System.Diagnostics.Debug.WriteLine($"EnvironmentUrl: '{EnvironmentUrl}'");

                if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(TenantId) || string.IsNullOrEmpty(EnvironmentUrl))
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: One or more settings are null/empty");
                    return false;
                }

                // Ensure temp directory exists for token cache
                try
                {
                    if (!System.IO.Directory.Exists("c:\\temp"))
                    {
                        System.IO.Directory.CreateDirectory("c:\\temp");
                    }
                }
                catch { /* Ignore errors creating directory */ }

                // Create client with explicit tenant in connection string
                _client = new CrmServiceClient(connectionString);

                if (!_client.IsReady)
                {
                    return false;
                }

                _organizationService = _client.OrganizationServiceProxy ??
                                      (IOrganizationService)_client.OrganizationWebProxyClient;

                return true;
            }
            catch (Exception)
            {
                return false;
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