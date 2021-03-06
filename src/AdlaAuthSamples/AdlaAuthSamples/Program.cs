﻿using System;
using System.IO;
using System.Threading;
using System.Security.Cryptography.X509Certificates;

using Microsoft.Rest;
using Microsoft.Rest.Azure.Authentication;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Azure.Management.DataLake.Analytics;
using Microsoft.Azure.Management.DataLake.Store;
using Microsoft.Azure.Graph.RBAC;


namespace AdlaAuthSamples
{
    // For more info:
    //   https://azure.microsoft.com/en-us/resources/samples/data-lake-analytics-dotnet-auth-options
    class Program
    {
        static void Main(string[] args)
        {
            string adlaAccountName = "<ADLA account name>";
            string resourceGroupName = "<resource group name>";
            string subscriptionId = "<subscription ID>";

            string domain = "<AAD tenant ID / domain>";
            var armTokenAudience = new Uri(@"https://management.core.windows.net/");
            var adlTokenAudience = new Uri(@"https://datalake.azure.net/");
            var aadTokenAudience = new Uri(@"https://graph.windows.net/");

            // ----------------------------------------
            // Perform authentication to get credentials
            // ----------------------------------------

            // INTERACTIVE WITH CACHE
            var tokenCache = new TokenCache();
            tokenCache.BeforeAccess = BeforeTokenCacheAccess;
            tokenCache.AfterAccess = AfterTokenCacheAccess;
            var armCreds = GetCredsInteractivePopup(domain, armTokenAudience, tokenCache, PromptBehavior.Auto);
            var adlCreds = GetCredsInteractivePopup(domain, adlTokenAudience, tokenCache, PromptBehavior.Auto);
            var aadCreds = GetCredsInteractivePopup(domain, aadTokenAudience, tokenCache, PromptBehavior.Auto);

            // INTERACTIVE WITHOUT CACHE
            // var armCreds = GetCredsInteractivePopup(domain, armTokenAudience, PromptBehavior.Auto);
            // var adlCreds = GetCredsInteractivePopup(domain, adlTokenAudience, PromptBehavior.Auto);
            // var aadCreds = GetCredsInteractivePopup(domain, aadTokenAudience, PromptBehavior.Auto);

            // NON-INTERACTIVE WITH SECRET KEY
            // string clientId = "<service principal / application client ID>";
            // string secretKey = "<service principal / application secret key>";
            // var armCreds = GetCredsServicePrincipalSecretKey(domain, armTokenAudience, clientId, secretKey);
            // var adlCreds = GetCredsServicePrincipalSecretKey(domain, adlTokenAudience, clientId, secretKey);
            // var aadCreds = GetCredsServicePrincipalSecretKey(domain, aadTokenAudience, clientId, secretKey);

            // NON-INTERACTIVE WITH CERT
            // string clientId = "<service principal / application client ID>";
            // var certificate = new X509Certificate2(@"<path to (PFX) certificate file>", "<certificate password>");
            // var armCreds = GetCredsServicePrincipalCertificate(domain, armTokenAudience, clientId, certificate);
            // var adlCreds = GetCredsServicePrincipalCertificate(domain, adlTokenAudience, clientId, certificate);
            // var aadCreds = GetCredsServicePrincipalCertificate(domain, aadTokenAudience, clientId, certificate);

            // ----------------------------------------
            // Create the REST clients using the credentials
            // ----------------------------------------

            var adlaAccountClient = new DataLakeAnalyticsAccountManagementClient(armCreds);
            adlaAccountClient.SubscriptionId = subscriptionId;

            var adlsAccountClient = new DataLakeStoreAccountManagementClient(armCreds);
            adlsAccountClient.SubscriptionId = subscriptionId;

            var adlaCatalogClient = new DataLakeAnalyticsCatalogManagementClient(adlCreds);
            var adlaJobClient = new DataLakeAnalyticsJobManagementClient(adlCreds);
            var adlsFileSystemClient = new DataLakeStoreFileSystemManagementClient(adlCreds);

            var graphClient = new GraphRbacManagementClient(aadCreds);
            graphClient.TenantID = domain;

            // ----------------------------------------
            // Perform operations with the REST clients
            // ----------------------------------------

            var account = adlaAccountClient.Account.Get(resourceGroupName, adlaAccountName);
            Console.WriteLine($"My account's location is: {account.Location}!");

            // string upn = "tim@contoso.com";
            // string displayName = graphClient.Users.Get(upn).DisplayName;
            // Console.WriteLine($"The display name for {upn} is {displayName}!");

            Console.ReadLine();
        }

        // The interactive samples reuse Azure PowerShell's client ID
        // For production code you should use your own client ids
        private static string azure_powershell_clientid = "1950a258-227b-4e31-a9cf-717495945fc2";
        
        /*
         *  Interactive: User popup
         *  (no token cache to reuse/save session state)
         */
        private static ServiceClientCredentials GetCredsInteractivePopup(string domain, Uri tokenAudience, PromptBehavior promptBehavior = PromptBehavior.Auto)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

            // The client id comes from Azure PowerShell
            // for production code you should use your own client id

            var clientSettings = new ActiveDirectoryClientSettings
            {
                ClientId = azure_powershell_clientid,
                ClientRedirectUri = new Uri("urn:ietf:wg:oauth:2.0:oob"),
                PromptBehavior = promptBehavior
            };

            var serviceSettings = ActiveDirectoryServiceSettings.Azure;
            serviceSettings.TokenAudience = tokenAudience;

            var creds = UserTokenProvider.LoginWithPromptAsync(domain, clientSettings, serviceSettings).GetAwaiter().GetResult();

            return creds;
        }

        /*
         *  Interactive: User popup
         *  (using a token cache to reuse/save session state)
         */
        private static ServiceClientCredentials GetCredsInteractivePopup(string domain, Uri tokenAudience, TokenCache tokenCache, PromptBehavior promptBehavior = PromptBehavior.Auto)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

            var clientSettings = new ActiveDirectoryClientSettings
            {
                ClientId = azure_powershell_clientid,
                ClientRedirectUri = new Uri("urn:ietf:wg:oauth:2.0:oob"),
                PromptBehavior = promptBehavior
            };

            var serviceSettings = ActiveDirectoryServiceSettings.Azure;
            serviceSettings.TokenAudience = tokenAudience;

            var creds = UserTokenProvider.LoginWithPromptAsync(domain, clientSettings, serviceSettings, tokenCache).GetAwaiter().GetResult();

            return creds;
        }

        private static void BeforeTokenCacheAccess(TokenCacheNotificationArgs args)
        {
            // NOTE: We recommend that you do NOT store the token cache in plain text -- don't use the code below as-is.
            //       Here's one example of a way to store the token cache in a slightly more secure way, using Data Protection APIs:
            //         http://www.cloudidentity.com/blog/2014/07/09/the-new-token-cache-in-adal-v2/
            string tokenCachePath = GetTokenCachePath();
            if (File.Exists(tokenCachePath))
            {
                args.TokenCache.Deserialize(File.ReadAllBytes(tokenCachePath));
            }
        }

        private static void AfterTokenCacheAccess(TokenCacheNotificationArgs args)
        {
            // NOTE: We recommend that you do NOT store the token cache in plain text -- don't use the code below as-is.
            //       Here's one example of a way to store the token cache in a slightly more secure way, using Data Protection APIs:
            //         http://www.cloudidentity.com/blog/2014/07/09/the-new-token-cache-in-adal-v2/
            string tokenCachePath = GetTokenCachePath();
            File.WriteAllBytes(tokenCachePath, args.TokenCache.Serialize());
        }

        private static string GetTokenCachePath()
        {
            // Pick any filesytem path you want for the token cache
            // The code below contructs a path location that is in 
            // the current user's local app data folder

            string folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string filename = nameof(AdlaAuthSamples) + ".tokencache";
            string tokenCachePath = System.IO.Path.Combine(folder, filename);
            return tokenCachePath;
        }

        /*
         *  Interactive: Device code login
         *  NOT YET SUPPORTED by Azure's .NET SDK authentication library
         */
        private static ServiceClientCredentials GetCredsDeviceCode()
        {
            throw new NotImplementedException("Azure SDK's .NET authentication library doesn't support device code login yet.");
        }

        /*
         *  Non-interactive: Service principal / application using a secret key
         *  Setup: https://docs.microsoft.com/en-us/azure/azure-resource-manager/resource-group-authenticate-service-principal#create-service-principal-with-password
         */
        private static ServiceClientCredentials GetCredsServicePrincipalSecretKey(string domain, Uri tokenAudience, string clientId, string secretKey)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

            var serviceSettings = ActiveDirectoryServiceSettings.Azure;
            serviceSettings.TokenAudience = tokenAudience;

            var creds = ApplicationTokenProvider.LoginSilentAsync(domain, clientId, secretKey, serviceSettings).GetAwaiter().GetResult();

            return creds;
        }

        /*
         *  Non-interactive: Service principal / application using a certificate
         *  Setup: https://docs.microsoft.com/en-us/azure/azure-resource-manager/resource-group-authenticate-service-principal#create-service-principal-with-self-signed-certificate
         */
        private static ServiceClientCredentials GetCredsServicePrincipalCertificate(string domain, Uri tokenAudience, string clientId, X509Certificate2 certificate)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

            var clientAssertionCertificate = new ClientAssertionCertificate(clientId, certificate);
            var serviceSettings = ActiveDirectoryServiceSettings.Azure;
            serviceSettings.TokenAudience = tokenAudience;

            var creds = ApplicationTokenProvider.LoginSilentWithCertificateAsync(domain, clientAssertionCertificate, serviceSettings).GetAwaiter().GetResult();

            return creds;
        }
    }
}
