// Diagnostic Tool for George Stow's Quote Loading Issue
// Compile with: csc /reference:packages\... DiagnoseGeorge.cs
// Or add to your solution as a Console App project

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Crm.Sdk.Messages;

namespace DHA.DSTC.Diagnostics
{
    class DiagnoseGeorge
    {
        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== DATAVERSE QUERY DIAGNOSTICS ===");
            Console.WriteLine("This tool tests query patterns to diagnose George Stow's quote loading issue");
            Console.WriteLine();
            Console.ResetColor();

            // Configuration from App.config
            string tenantId = "28f0e92f-184e-4166-84cd-af41a0b93f83";
            string clientId = "41cf8aa2-60ea-404e-b549-32491b2060dc";

            // Environment selection
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Select environment:");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("1. Production (https://dhapd.crm11.dynamics.com)");
            Console.WriteLine("2. Dev (https://dhapd-dev.crm11.dynamics.com)");
            Console.ResetColor();
            Console.Write("Enter choice (1 or 2): ");
            string envChoice = Console.ReadLine();

            string orgUrl = envChoice == "2"
                ? "https://dhapd-dev.crm11.dynamics.com"
                : "https://dhapd.crm11.dynamics.com";

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Using environment: {orgUrl}");
            Console.ResetColor();
            Console.WriteLine();

            // Impersonation
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Do you want to test as a specific user (impersonation)?");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("NOTE: You'll log in as yourself, but queries will run as the target user");
            Console.ResetColor();
            Console.Write("Enter 'y' to test as George Stow, or press Enter to test as yourself: ");
            string impersonate = Console.ReadLine();

            string targetUserEmail = null;
            if (impersonate?.ToLower() == "y")
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Common users:");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("  George Stow: George.Stow@dhatransport.co.uk");
                Console.ResetColor();
                Console.Write("Enter the user's email address: ");
                targetUserEmail = Console.ReadLine();
            }

            // Connect
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Connecting to Dataverse...");
            Console.ResetColor();

            string connectionString = $"AuthType=OAuth;Url={orgUrl};AppId={clientId};RedirectUri=http://localhost;LoginPrompt=Auto;";

            CrmServiceClient serviceClient = null;
            try
            {
                serviceClient = new CrmServiceClient(connectionString);

                if (!serviceClient.IsReady)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ERROR] Connection failed: {serviceClient.LastCrmError}");
                    Console.ResetColor();
                    Console.WriteLine("Press Enter to exit...");
                    Console.ReadLine();
                    return;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[OK] Connected successfully");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"    User: {serviceClient.OAuthUserId}");
                Console.ResetColor();

                // Get WhoAmI info
                var whoAmIRequest = new WhoAmIRequest();
                var whoAmIResponse = (WhoAmIResponse)serviceClient.Execute(whoAmIRequest);
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"    User ID: {whoAmIResponse.UserId}");
                Console.WriteLine($"    Business Unit: {whoAmIResponse.BusinessUnitId}");
                Console.ResetColor();

                // Impersonation
                if (!string.IsNullOrWhiteSpace(targetUserEmail))
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Looking up target user: {targetUserEmail}...");
                    Console.ResetColor();

                    var userQuery = new QueryExpression("systemuser")
                    {
                        ColumnSet = new ColumnSet("systemuserid", "fullname", "domainname", "businessunitid")
                    };
                    userQuery.Criteria.AddCondition("domainname", ConditionOperator.Equal, targetUserEmail);

                    var userResult = serviceClient.RetrieveMultiple(userQuery);

                    if (userResult.Entities.Count == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[ERROR] User not found: {targetUserEmail}");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("Continuing as yourself instead...");
                        Console.ResetColor();
                    }
                    else
                    {
                        var targetUser = userResult.Entities[0];
                        var targetUserId = targetUser.Id;
                        var targetFullName = targetUser.GetAttributeValue<string>("fullname");

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[OK] Found user: {targetFullName}");
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.WriteLine($"    User ID: {targetUserId}");
                        Console.ResetColor();

                        // Set impersonation
                        serviceClient.CallerId = targetUserId;

                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"[IMPERSONATION] All queries will run as: {targetFullName}");
                        Console.ResetColor();
                    }
                }

                // Run diagnostic tests
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=== RUNNING DIAGNOSTIC TESTS ===");
                Console.ResetColor();
                Console.WriteLine();

                RunDiagnostics(serviceClient);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Script completed. Press Enter to exit...");
                Console.ResetColor();
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Console.ResetColor();
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
            }
            finally
            {
                serviceClient?.Dispose();
            }
        }

        static void RunDiagnostics(CrmServiceClient serviceClient)
        {
            int test1Count = 0, test2Count = 0, test3Count = 0, test4Count = 0;

            // Test 1: Baseline - all quotes
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Test 1: Counting all quotes (no filters)...");
            Console.ResetColor();
            try
            {
                var query1 = new QueryExpression("quote")
                {
                    ColumnSet = new ColumnSet("quoteid"),
                    PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
                };

                var result1 = serviceClient.RetrieveMultiple(query1);
                test1Count = result1.Entities.Count;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  Result: {test1Count} quotes");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Error: {ex.Message}");
                Console.ResetColor();
            }

            // Test 2: Status filter only
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Test 2: Quotes with status NOT IN (0,2,3,4)...");
            Console.ResetColor();
            try
            {
                var query2 = new QueryExpression("quote")
                {
                    ColumnSet = new ColumnSet("quoteid"),
                    PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
                };
                query2.Criteria.AddCondition("statuscode", ConditionOperator.NotIn, new object[] { 0, 2, 3, 4 });

                var result2 = serviceClient.RetrieveMultiple(query2);
                test2Count = result2.Entities.Count;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  Result: {test2Count} quotes");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Error: {ex.Message}");
                Console.ResetColor();
            }

            // Test 3: NULL filter
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Test 3: Quotes with isc_projectnumbervisible IS NULL...");
            Console.ResetColor();
            try
            {
                var query3 = new QueryExpression("quote")
                {
                    ColumnSet = new ColumnSet("quoteid", "isc_projectnumbervisible"),
                    PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
                };
                query3.Criteria.AddCondition("isc_projectnumbervisible", ConditionOperator.Null);

                var result3 = serviceClient.RetrieveMultiple(query3);
                test3Count = result3.Entities.Count;

                if (test3Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Result: {test3Count} quotes");
                    Console.WriteLine("  WARNING: NULL filter returns 0 results - this is the bug!");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  Result: {test3Count} quotes");
                }
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Error: {ex.Message}");
                Console.ResetColor();
            }

            // Test 4: Combined filter
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Test 4: Combined filter (status + NULL)...");
            Console.ResetColor();
            try
            {
                var query4 = new QueryExpression("quote")
                {
                    ColumnSet = new ColumnSet("quoteid"),
                    PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
                };
                query4.Criteria.AddCondition("statuscode", ConditionOperator.NotIn, new object[] { 0, 2, 3, 4 });
                query4.Criteria.AddCondition("isc_projectnumbervisible", ConditionOperator.Null);

                var result4 = serviceClient.RetrieveMultiple(query4);
                test4Count = result4.Entities.Count;

                if (test4Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                }
                Console.WriteLine($"  Result: {test4Count} quotes");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Error: {ex.Message}");
                Console.ResetColor();
            }

            // Analysis
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== ANALYSIS ===");
            Console.ResetColor();
            Console.WriteLine();

            if (test3Count == 0 && test1Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[CRITICAL] NULL filter on isc_projectnumbervisible returns 0 results");
                Console.WriteLine("This is the root cause of the issue!");
                Console.ResetColor();
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Possible causes:");
                Console.WriteLine("  1. Field-Level Security restricting NULL queries");
                Console.WriteLine("  2. Custom field behavior in Dataverse");
                Console.WriteLine("  3. Business Unit restrictions");
                Console.WriteLine("  4. Field stored as empty string instead of NULL");
                Console.ResetColor();
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Recommendation: Use the fallback query (Test 2) and filter in C# code");
                Console.WriteLine("This fallback is already implemented in QuoteService.cs lines 98-118");
                Console.ResetColor();
            }
            else if (test4Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[OK] Queries are working correctly for this user");
                Console.WriteLine($"Expected {test4Count} quotes to be displayed");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[UNKNOWN] Unexpected results - manual investigation needed");
                Console.ResetColor();
            }
        }
    }
}
