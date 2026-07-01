using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Utilities;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace DHA.DSTC.WPF.Services
{
    public class QuoteService
    {
        private readonly DataverseConnector _connector;
        private readonly string _entityName = "quote";

        public QuoteService(DataverseConnector connector)
        {
            _connector = connector;
        }

        /// <summary>
        /// Diagnostic method to investigate why a specific quote is not appearing in the app.
        /// Call this method with a quote number (e.g., "Q27557") to see its raw Dataverse data.
        /// </summary>
        public void DiagnoseQuote(string quoteNumber)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"");
                System.Diagnostics.Debug.WriteLine($"╔══════════════════════════════════════════════════════════════════╗");
                System.Diagnostics.Debug.WriteLine($"║  QUOTE DIAGNOSTIC: {quoteNumber,-45} ║");
                System.Diagnostics.Debug.WriteLine($"╚══════════════════════════════════════════════════════════════════╝");

                if (!_connector.Connect())
                {
                    System.Diagnostics.Debug.WriteLine("DIAGNOSTIC ERROR: Failed to connect to Dataverse");
                    return;
                }

                // Query for the specific quote WITHOUT any filters
                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(
                        "name",
                        "quotenumber",
                        "customerid",
                        "statuscode",
                        "statecode",
                        "isc_projectnumbervisible",
                        "createdon",
                        "modifiedon"
                    )
                };

                // Extract numeric part from quote number for flexible matching
                var numericPart = System.Text.RegularExpressions.Regex.Match(quoteNumber, @"\d+").Value;
                
                var searchFilter = new FilterExpression(LogicalOperator.Or);
                searchFilter.AddCondition("quotenumber", ConditionOperator.Equal, quoteNumber);
                searchFilter.AddCondition("quotenumber", ConditionOperator.Like, $"%{quoteNumber}%");
                if (!string.IsNullOrEmpty(numericPart))
                {
                    searchFilter.AddCondition("quotenumber", ConditionOperator.Like, $"%{numericPart}%");
                }
                
                query.Criteria = searchFilter;

                // Add link to customer to get customer name
                var customerLink = new LinkEntity
                {
                    LinkFromEntityName = "quote",
                    LinkFromAttributeName = "customerid",
                    LinkToEntityName = "account",
                    LinkToAttributeName = "accountid",
                    Columns = new ColumnSet("name"),
                    EntityAlias = "customer",
                    JoinOperator = JoinOperator.LeftOuter
                };
                query.LinkEntities.Add(customerLink);

                System.Diagnostics.Debug.WriteLine($"DIAGNOSTIC: Searching for quote with number matching '{quoteNumber}' or '{numericPart}'");
                System.Diagnostics.Debug.WriteLine($"DIAGNOSTIC: Query has NO status/project filters - should find quote regardless of state");

                var result = _connector._orgService.RetrieveMultiple(query);
                var entities = result?.Entities?.ToList() ?? new List<Entity>();

                System.Diagnostics.Debug.WriteLine($"DIAGNOSTIC: Found {entities.Count} matching quote(s)");
                System.Diagnostics.Debug.WriteLine($"");

                if (entities.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️  NO QUOTE FOUND with number '{quoteNumber}'");
                    System.Diagnostics.Debug.WriteLine($"    Possible reasons:");
                    System.Diagnostics.Debug.WriteLine($"    - Quote does not exist in Dataverse");
                    System.Diagnostics.Debug.WriteLine($"    - Quote number format is different (try exact format from Dataverse)");
                    System.Diagnostics.Debug.WriteLine($"    - User does not have access to this quote");
                    return;
                }

                foreach (var entity in entities)
                {
                    System.Diagnostics.Debug.WriteLine($"┌─────────────────────────────────────────────────────────────────┐");
                    System.Diagnostics.Debug.WriteLine($"│ QUOTE DETAILS                                                   │");
                    System.Diagnostics.Debug.WriteLine($"├─────────────────────────────────────────────────────────────────┤");
                    
                    // Basic info
                    var name = entity.Contains("name") ? entity["name"]?.ToString() : "(null)";
                    var number = entity.Contains("quotenumber") ? entity["quotenumber"]?.ToString() : "(null)";
                    var id = entity.Id;
                    
                    System.Diagnostics.Debug.WriteLine($"│ Quote ID:          {id,-43} │");
                    System.Diagnostics.Debug.WriteLine($"│ Quote Number:      {number,-43} │");
                    System.Diagnostics.Debug.WriteLine($"│ Name:              {name,-43} │");
                    
                    // Customer
                    string customerName = "(null)";
                    if (entity.Contains("customer.name"))
                    {
                        var aliasedValue = entity.GetAttributeValue<AliasedValue>("customer.name");
                        customerName = aliasedValue?.Value?.ToString() ?? "(aliased null)";
                    }
                    else if (entity.Contains("customerid"))
                    {
                        var customerRef = entity.GetAttributeValue<EntityReference>("customerid");
                        customerName = customerRef?.Name ?? "(ref name null)";
                    }
                    System.Diagnostics.Debug.WriteLine($"│ Customer:          {customerName,-43} │");
                    
                    // Status codes
                    var statusCode = entity.Contains("statuscode") 
                        ? entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value.ToString() 
                        : "(null)";
                    var stateCode = entity.Contains("statecode") 
                        ? entity.GetAttributeValue<OptionSetValue>("statecode")?.Value.ToString() 
                        : "(null)";
                    
                    System.Diagnostics.Debug.WriteLine($"│ Status Code:       {statusCode,-43} │");
                    System.Diagnostics.Debug.WriteLine($"│ State Code:        {stateCode,-43} │");
                    
                    // Status code meaning
                    string statusMeaning = GetStatusCodeMeaning(entity.Contains("statuscode") 
                        ? entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? -1 
                        : -1);
                    System.Diagnostics.Debug.WriteLine($"│ Status Meaning:    {statusMeaning,-43} │");
                    
                    // Project number visible
                    var projectVisible = entity.Contains("isc_projectnumbervisible") 
                        ? entity["isc_projectnumbervisible"]?.ToString() ?? "(attribute present but null)"
                        : "(attribute not present)";
                    System.Diagnostics.Debug.WriteLine($"│ ProjectNumVisible: {projectVisible,-43} │");
                    
                    // Dates
                    var createdOn = entity.Contains("createdon") 
                        ? entity.GetAttributeValue<DateTime>("createdon").ToString("yyyy-MM-dd HH:mm:ss") 
                        : "(null)";
                    var modifiedOn = entity.Contains("modifiedon") 
                        ? entity.GetAttributeValue<DateTime>("modifiedon").ToString("yyyy-MM-dd HH:mm:ss") 
                        : "(null)";
                    System.Diagnostics.Debug.WriteLine($"│ Created On:        {createdOn,-43} │");
                    System.Diagnostics.Debug.WriteLine($"│ Modified On:       {modifiedOn,-43} │");
                    
                    System.Diagnostics.Debug.WriteLine($"├─────────────────────────────────────────────────────────────────┤");
                    System.Diagnostics.Debug.WriteLine($"│ FILTER ANALYSIS                                                 │");
                    System.Diagnostics.Debug.WriteLine($"├─────────────────────────────────────────────────────────────────┤");
                    
                    // Analyze why it might be filtered out
                    int statusCodeValue = entity.Contains("statuscode") 
                        ? entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? -1 
                        : -1;
                    
                    bool statusExcluded = statusCodeValue == 0 || statusCodeValue == 2 || statusCodeValue == 3 || statusCodeValue == 4;
                    bool hasProjectNumber = entity.Contains("isc_projectnumbervisible") && 
                                           !string.IsNullOrWhiteSpace(entity["isc_projectnumbervisible"]?.ToString());
                    
                    string statusCheck = statusExcluded 
                        ? $"❌ EXCLUDED (statuscode {statusCodeValue} is in exclusion list [0,2,3,4])"
                        : $"✓ PASS (statuscode {statusCodeValue} is NOT excluded)";
                    System.Diagnostics.Debug.WriteLine($"│ Status Filter:     {statusCheck,-43} │");
                    
                    string projectCheck = hasProjectNumber 
                        ? $"❌ EXCLUDED (isc_projectnumbervisible is NOT null: '{projectVisible}')"
                        : $"✓ PASS (isc_projectnumbervisible is null/empty)";
                    System.Diagnostics.Debug.WriteLine($"│ Project Filter:    {(projectCheck.Length > 43 ? projectCheck.Substring(0, 40) + "..." : projectCheck),-43} │");
                    
                    // Overall verdict
                    System.Diagnostics.Debug.WriteLine($"├─────────────────────────────────────────────────────────────────┤");
                    if (statusExcluded || hasProjectNumber)
                    {
                        System.Diagnostics.Debug.WriteLine($"│ ⚠️  VERDICT: Quote is FILTERED OUT and will NOT appear in app   │");
                        if (statusExcluded)
                            System.Diagnostics.Debug.WriteLine($"│    Reason: Status code {statusCodeValue} ({statusMeaning}) is excluded    │");
                        if (hasProjectNumber)
                            System.Diagnostics.Debug.WriteLine($"│    Reason: Project number visible field is not null            │");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"│ ✓ VERDICT: Quote SHOULD appear in the app                       │");
                        System.Diagnostics.Debug.WriteLine($"│    If it's not appearing, check IsActive logic in Quote.FromEntity()│");
                    }
                    System.Diagnostics.Debug.WriteLine($"└─────────────────────────────────────────────────────────────────┘");
                    System.Diagnostics.Debug.WriteLine($"");
                    
                    // Try converting to Quote object and see if IsActive is set correctly
                    var quote = Quote.FromEntity(entity);
                    if (quote != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Quote.FromEntity conversion result:");
                        System.Diagnostics.Debug.WriteLine($"  - Id: {quote.Id}");
                        System.Diagnostics.Debug.WriteLine($"  - Name: {quote.Name}");
                        System.Diagnostics.Debug.WriteLine($"  - QuoteNumber: {quote.QuoteNumber}");
                        System.Diagnostics.Debug.WriteLine($"  - IsActive: {quote.IsActive}");
                        System.Diagnostics.Debug.WriteLine($"  - StatusCode: {quote.StatusCode}");
                        System.Diagnostics.Debug.WriteLine($"  - ProjectNumberVisible: {quote.ProjectNumberVisible ?? "(null)"}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"WARNING: Quote.FromEntity returned null!");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"");
                System.Diagnostics.Debug.WriteLine($"═══════════════════════════════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DIAGNOSTIC ERROR: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"DIAGNOSTIC STACK: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Diagnostic method that returns a formatted string explaining why a specific quote 
        /// is or isn't appearing in the app. Useful for UI display.
        /// </summary>
        public string DiagnoseQuoteToString(string quoteNumber)
        {
            var sb = new System.Text.StringBuilder();
            
            try
            {
                sb.AppendLine($"╔══════════════════════════════════════════════════════════════════╗");
                sb.AppendLine($"║  QUOTE DIAGNOSTIC: {quoteNumber,-45} ║");
                sb.AppendLine($"╚══════════════════════════════════════════════════════════════════╝");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                if (!_connector.Connect())
                {
                    sb.AppendLine("❌ DIAGNOSTIC ERROR: Failed to connect to Dataverse");
                    return sb.ToString();
                }

                sb.AppendLine("✓ Successfully connected to Dataverse");
                sb.AppendLine();

                // Query for the specific quote WITHOUT any filters
                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(
                        "name",
                        "quotenumber",
                        "customerid",
                        "statuscode",
                        "statecode",
                        "isc_projectnumbervisible",
                        "createdon",
                        "modifiedon"
                    )
                };

                // Extract numeric part from quote number for flexible matching
                var numericPart = System.Text.RegularExpressions.Regex.Match(quoteNumber, @"\d+").Value;
                
                var searchFilter = new FilterExpression(LogicalOperator.Or);
                searchFilter.AddCondition("quotenumber", ConditionOperator.Equal, quoteNumber);
                searchFilter.AddCondition("quotenumber", ConditionOperator.Like, $"%{quoteNumber}%");
                if (!string.IsNullOrEmpty(numericPart))
                {
                    searchFilter.AddCondition("quotenumber", ConditionOperator.Like, $"%{numericPart}%");
                }
                
                query.Criteria = searchFilter;

                // Add link to customer to get customer name
                var customerLink = new LinkEntity
                {
                    LinkFromEntityName = "quote",
                    LinkFromAttributeName = "customerid",
                    LinkToEntityName = "account",
                    LinkToAttributeName = "accountid",
                    Columns = new ColumnSet("name"),
                    EntityAlias = "customer",
                    JoinOperator = JoinOperator.LeftOuter
                };
                query.LinkEntities.Add(customerLink);

                sb.AppendLine($"Searching for quote with number matching '{quoteNumber}' or '{numericPart}'");
                sb.AppendLine($"Query has NO status/project filters - should find quote regardless of state");
                sb.AppendLine();

                var result = _connector._orgService.RetrieveMultiple(query);
                var entities = result?.Entities?.ToList() ?? new List<Entity>();

                sb.AppendLine($"Found {entities.Count} matching quote(s)");
                sb.AppendLine();

                if (entities.Count == 0)
                {
                    sb.AppendLine($"⚠️  NO QUOTE FOUND with number '{quoteNumber}'");
                    sb.AppendLine($"    Possible reasons:");
                    sb.AppendLine($"    - Quote does not exist in Dataverse");
                    sb.AppendLine($"    - Quote number format is different (try exact format from Dataverse)");
                    sb.AppendLine($"    - User does not have access to this quote");
                    return sb.ToString();
                }

                foreach (var entity in entities)
                {
                    sb.AppendLine($"┌─────────────────────────────────────────────────────────────────┐");
                    sb.AppendLine($"│ QUOTE DETAILS                                                   │");
                    sb.AppendLine($"├─────────────────────────────────────────────────────────────────┤");
                    
                    // Basic info
                    var name = entity.Contains("name") ? entity["name"]?.ToString() : "(null)";
                    var number = entity.Contains("quotenumber") ? entity["quotenumber"]?.ToString() : "(null)";
                    var id = entity.Id;
                    
                    sb.AppendLine($"│ Quote ID:          {id,-43} │");
                    sb.AppendLine($"│ Quote Number:      {number,-43} │");
                    sb.AppendLine($"│ Name:              {TruncateForDisplay(name, 43),-43} │");
                    
                    // Customer
                    string customerName = "(null)";
                    if (entity.Contains("customer.name"))
                    {
                        var aliasedValue = entity.GetAttributeValue<AliasedValue>("customer.name");
                        customerName = aliasedValue?.Value?.ToString() ?? "(aliased null)";
                    }
                    else if (entity.Contains("customerid"))
                    {
                        var customerRef = entity.GetAttributeValue<EntityReference>("customerid");
                        customerName = customerRef?.Name ?? "(ref name null)";
                    }
                    sb.AppendLine($"│ Customer:          {TruncateForDisplay(customerName, 43),-43} │");
                    
                    // Status codes
                    var statusCode = entity.Contains("statuscode") 
                        ? entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value.ToString() 
                        : "(null)";
                    var stateCode = entity.Contains("statecode") 
                        ? entity.GetAttributeValue<OptionSetValue>("statecode")?.Value.ToString() 
                        : "(null)";
                    
                    sb.AppendLine($"│ Status Code:       {statusCode,-43} │");
                    sb.AppendLine($"│ State Code:        {stateCode,-43} │");
                    
                    // Status code meaning
                    string statusMeaning = GetStatusCodeMeaning(entity.Contains("statuscode") 
                        ? entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? -1 
                        : -1);
                    sb.AppendLine($"│ Status Meaning:    {statusMeaning,-43} │");
                    
                    // Project number visible
                    var projectVisible = entity.Contains("isc_projectnumbervisible") 
                        ? entity["isc_projectnumbervisible"]?.ToString() ?? "(attribute present but null)"
                        : "(attribute not present)";
                    sb.AppendLine($"│ ProjectNumVisible: {TruncateForDisplay(projectVisible, 43),-43} │");
                    
                    // Dates
                    var createdOn = entity.Contains("createdon") 
                        ? entity.GetAttributeValue<DateTime>("createdon").ToString("yyyy-MM-dd HH:mm:ss") 
                        : "(null)";
                    var modifiedOn = entity.Contains("modifiedon") 
                        ? entity.GetAttributeValue<DateTime>("modifiedon").ToString("yyyy-MM-dd HH:mm:ss") 
                        : "(null)";
                    sb.AppendLine($"│ Created On:        {createdOn,-43} │");
                    sb.AppendLine($"│ Modified On:       {modifiedOn,-43} │");
                    
                    sb.AppendLine($"├─────────────────────────────────────────────────────────────────┤");
                    sb.AppendLine($"│ FILTER ANALYSIS                                                 │");
                    sb.AppendLine($"├─────────────────────────────────────────────────────────────────┤");
                    
                    // Analyze why it might be filtered out
                    int statusCodeValue = entity.Contains("statuscode") 
                        ? entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? -1 
                        : -1;
                    
                    bool statusExcluded = statusCodeValue == 0 || statusCodeValue == 2 || statusCodeValue == 3 || statusCodeValue == 4;
                    bool hasProjectNumber = entity.Contains("isc_projectnumbervisible") && 
                                           !string.IsNullOrWhiteSpace(entity["isc_projectnumbervisible"]?.ToString());
                    
                    string statusCheck = statusExcluded 
                        ? $"❌ EXCLUDED (statuscode {statusCodeValue} is in exclusion list [0,2,3,4])"
                        : $"✓ PASS (statuscode {statusCodeValue} is NOT excluded)";
                    sb.AppendLine($"│ Status Filter:                                                  │");
                    sb.AppendLine($"│   {statusCheck,-61} │");
                    
                    string projectCheck = hasProjectNumber 
                        ? $"❌ EXCLUDED (isc_projectnumbervisible is NOT null)"
                        : $"✓ PASS (isc_projectnumbervisible is null/empty)";
                    sb.AppendLine($"│ Project Filter:                                                 │");
                    sb.AppendLine($"│   {projectCheck,-61} │");
                    
                    // Overall verdict
                    sb.AppendLine($"├─────────────────────────────────────────────────────────────────┤");
                    sb.AppendLine($"│ VERDICT                                                         │");
                    sb.AppendLine($"├─────────────────────────────────────────────────────────────────┤");
                    if (statusExcluded || hasProjectNumber)
                    {
                        sb.AppendLine($"│ ⚠️  Quote is FILTERED OUT and will NOT appear in the app        │");
                        if (statusExcluded)
                            sb.AppendLine($"│    Reason: Status code {statusCodeValue} ({statusMeaning}) is excluded       │");
                        if (hasProjectNumber)
                            sb.AppendLine($"│    Reason: Project number visible field is not null            │");
                    }
                    else
                    {
                        sb.AppendLine($"│ ✓ Quote SHOULD appear in the app                                │");
                        sb.AppendLine($"│   If it's not appearing, check IsActive logic in Quote.FromEntity│");
                    }
                    sb.AppendLine($"└─────────────────────────────────────────────────────────────────┘");
                    sb.AppendLine();
                    
                    // Try converting to Quote object and see if IsActive is set correctly
                    var quote = Quote.FromEntity(entity);
                    if (quote != null)
                    {
                        sb.AppendLine($"Quote.FromEntity Conversion Result:");
                        sb.AppendLine($"  - Id: {quote.Id}");
                        sb.AppendLine($"  - Name: {quote.Name}");
                        sb.AppendLine($"  - QuoteNumber: {quote.QuoteNumber}");
                        sb.AppendLine($"  - IsActive: {quote.IsActive}");
                        sb.AppendLine($"  - StatusCode: {quote.StatusCode}");
                        sb.AppendLine($"  - ProjectNumberVisible: {quote.ProjectNumberVisible ?? "(null)"}");
                        
                        if (!quote.IsActive)
                        {
                            sb.AppendLine();
                            sb.AppendLine($"  ⚠️  IsActive is FALSE - Quote will not appear even if it passes filters!");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"⚠️  WARNING: Quote.FromEntity returned null!");
                    }
                    
                    sb.AppendLine();
                }
                
                sb.AppendLine($"═══════════════════════════════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"❌ DIAGNOSTIC ERROR: {ex.GetType().Name} - {ex.Message}");
                sb.AppendLine($"Stack Trace: {ex.StackTrace}");
            }
            
            return sb.ToString();
        }

        private string TruncateForDisplay(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength - 3) + "...";
        }

        private string GetStatusCodeMeaning(int statusCode)
        {
            switch (statusCode)
            {
                case 0: return "Draft/Inactive";
                case 1: return "In Progress";
                case 2: return "Won";
                case 3: return "Lost";
                case 4: return "Closed";
                default: return $"Unknown ({statusCode})";
            }
        }

        public List<Quote> GetQuotes()
        {
            try
            {
                FileLogger.Info("=== GetQuotes: Starting quote retrieval ===");
                System.Diagnostics.Debug.WriteLine("=== GetQuotes: Starting quote retrieval ===");

                // Ensure connection before attempting to retrieve data
                if (!_connector.Connect())
                {
                    FileLogger.Warn("GetQuotes: Failed to connect to Dataverse");
                    System.Diagnostics.Debug.WriteLine("GetQuotes: Failed to connect to Dataverse");
                    MessageBox.Show("Failed to connect to Dataverse",
                        "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return new List<Quote>();
                }

                FileLogger.Info("GetQuotes: Connection successful");
                System.Diagnostics.Debug.WriteLine("GetQuotes: Connection successful");

                // Use specific columns instead of retrieving all
                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(
                        "name",
                        "quotenumber",
                        "customerid",
                        "statuscode",
                        "isc_projectnumbervisible"
                    ),
                    Orders = {
                        new OrderExpression("name", OrderType.Ascending)
                    }
                };

                // Add link to customer to get customer name
                var customerLink = new LinkEntity
                {
                    LinkFromEntityName = "quote",
                    LinkFromAttributeName = "customerid",
                    LinkToEntityName = "account",
                    LinkToAttributeName = "accountid",
                    Columns = new ColumnSet("name"),
                    EntityAlias = "customer",
                    JoinOperator = JoinOperator.LeftOuter
                };
                query.LinkEntities.Add(customerLink);

                // Filter for active quotes only (exclude Draft/Inactive, Won, Lost, Closed)
                // AND where project number is not visible (null/empty)
                var statusFilter = new FilterExpression(LogicalOperator.And);
                statusFilter.AddCondition("statuscode", ConditionOperator.NotIn, new object[] { 0, 2, 3, 4 }); // Exclude Draft, Won, Lost, Closed
                statusFilter.AddCondition("isc_projectnumbervisible", ConditionOperator.Null);

                query.Criteria = statusFilter;

                System.Diagnostics.Debug.WriteLine("GetQuotes: Query filters - statuscode NOT IN (0,2,3,4), isc_projectnumbervisible = NULL");

                // Increase page size to get more quotes initially
                query.PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1
                };

                FileLogger.Info("GetQuotes: Executing query to Dataverse...");
                System.Diagnostics.Debug.WriteLine("GetQuotes: Executing query to Dataverse...");
                var result = _connector._orgService.RetrieveMultiple(query);

                if (result?.Entities == null)
                {
                    FileLogger.Warn("GetQuotes: No data returned from Dataverse (result.Entities is null)");
                    System.Diagnostics.Debug.WriteLine("GetQuotes: No data returned from Dataverse (result.Entities is null)");
                    MessageBox.Show("No data returned from Dataverse",
                        "Data Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return new List<Quote>();
                }

                var entities = result.Entities.ToList();
                FileLogger.Info($"GetQuotes: Retrieved {entities.Count} raw entities from Dataverse");
                System.Diagnostics.Debug.WriteLine($"GetQuotes: Retrieved {entities.Count} raw entities from Dataverse");

                // FALLBACK: If NULL filter returns 0 results, try without it (some environments have issues with NULL checks)
                if (entities.Count == 0)
                {
                    FileLogger.Info("GetQuotes: NULL filter returned 0 results, trying fallback query without project number filter...");
                    System.Diagnostics.Debug.WriteLine("GetQuotes: NULL filter returned 0 results, trying fallback query without project number filter...");

                    var fallbackQuery = new QueryExpression(_entityName)
                    {
                        ColumnSet = new ColumnSet("name", "quotenumber", "customerid", "statuscode", "isc_projectnumbervisible"),
                        Orders = { new OrderExpression("name", OrderType.Ascending) }
                    };
                    fallbackQuery.LinkEntities.Add(customerLink);

                    var fallbackFilter = new FilterExpression(LogicalOperator.And);
                    fallbackFilter.AddCondition("statuscode", ConditionOperator.NotIn, new object[] { 0, 2, 3, 4 });
                    fallbackQuery.Criteria = fallbackFilter;
                    fallbackQuery.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };

                    var fallbackResult = _connector._orgService.RetrieveMultiple(fallbackQuery);
                    entities = fallbackResult?.Entities?.ToList() ?? new List<Entity>();
                    System.Diagnostics.Debug.WriteLine($"GetQuotes: Fallback query retrieved {entities.Count} entities (project number filtering will be done in C# code)");
                }

                // Log details of each entity before conversion
                for (int i = 0; i < Math.Min(entities.Count, 10); i++)
                {
                    var entity = entities[i];
                    var quoteName = entity.Contains("name") ? entity["name"]?.ToString() : "N/A";
                    var quoteNumber = entity.Contains("quotenumber") ? entity["quotenumber"]?.ToString() : "N/A";
                    var statusCode = entity.Contains("statuscode") ? entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value.ToString() : "N/A";
                    var projectVisible = entity.Contains("isc_projectnumbervisible") ? entity["isc_projectnumbervisible"]?.ToString() : "null";
                    System.Diagnostics.Debug.WriteLine($"  Entity {i + 1}: Name={quoteName}, Number={quoteNumber}, StatusCode={statusCode}, ProjectVisible={projectVisible}");
                }
                if (entities.Count > 10)
                {
                    System.Diagnostics.Debug.WriteLine($"  ... and {entities.Count - 10} more entities");
                }

                var quotes = entities
                    .Select(Quote.FromEntity)
                    .Where(q => q != null && q.IsActive)
                    .ToList();

                // Debug logging
                FileLogger.Info($"GetQuotes: After conversion and filtering - {quotes.Count} active quotes (from {entities.Count} total entities)");
                System.Diagnostics.Debug.WriteLine($"GetQuotes: After conversion and filtering - {quotes.Count} active quotes (from {entities.Count} total entities)");

                if (entities.Count > 0 && quotes.Count == 0)
                {
                    FileLogger.Warn("GetQuotes retrieved entities but none converted to active quotes - check Quote.FromEntity() logic");
                    System.Diagnostics.Debug.WriteLine("WARNING: GetQuotes retrieved entities but none converted to active quotes - check Quote.FromEntity() logic");
                }

                FileLogger.Info("=== GetQuotes: Completed ===");
                System.Diagnostics.Debug.WriteLine("=== GetQuotes: Completed ===");
                return quotes;
            }
            catch (Exception ex)
            {
                FileLogger.Error("GetQuotes FAILED", ex);
                System.Diagnostics.Debug.WriteLine($"GetQuotes ERROR: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"GetQuotes STACK TRACE: {ex.StackTrace}");
                throw;
            }
        }

        public List<Quote> SearchQuotes(string searchTerm)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== SearchQuotes: Starting search for term '{searchTerm}' ===");

                if (!_connector.Connect())
                {
                    System.Diagnostics.Debug.WriteLine("SearchQuotes: Failed to connect to Dataverse");
                    return new List<Quote>();
                }

                System.Diagnostics.Debug.WriteLine("SearchQuotes: Connection successful");

                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    System.Diagnostics.Debug.WriteLine("SearchQuotes: Empty search term, delegating to GetQuotes()");
                    return GetQuotes();
                }

                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(
                        "name",
                        "quotenumber",
                        "customerid",
                        "statuscode",
                        "isc_projectnumbervisible"
                    ),
                    Orders = {
                        new OrderExpression("name", OrderType.Ascending)
                    }
                };

                // Add link to customer to get customer name
                var customerLink = new LinkEntity
                {
                    LinkFromEntityName = "quote",
                    LinkFromAttributeName = "customerid",
                    LinkToEntityName = "account",
                    LinkToAttributeName = "accountid",
                    Columns = new ColumnSet("name"),
                    EntityAlias = "customer",
                    JoinOperator = JoinOperator.LeftOuter
                };
                query.LinkEntities.Add(customerLink);

                // Create OR search conditions
                var searchGroup = new FilterExpression(LogicalOperator.Or);

                // Always search the full term against quote name
                searchGroup.AddCondition("name", ConditionOperator.Like, $"%{searchTerm}%");

                // Check if search term looks like a quote number
                var quoteNumberMatch = System.Text.RegularExpressions.Regex.Match(
                    searchTerm.Trim(),
                    @"^(QU|Q|)(\d+)"
                );

                if (quoteNumberMatch.Success)
                {
                    // Extract the quote number pattern
                    string quoteNumber = quoteNumberMatch.Groups[2].Value;

                    System.Diagnostics.Debug.WriteLine($"SearchQuotes: Detected quote number pattern, extracted: {quoteNumber}");

                    // Search for this number in the quote number field
                    searchGroup.AddCondition("quotenumber", ConditionOperator.Like, $"%{quoteNumber}%");
                    searchGroup.AddCondition("quotenumber", ConditionOperator.Like, $"%{searchTerm}%");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SearchQuotes: Not a quote number pattern, searching as general term");
                    // If it's not a numeric search, also search quote number field with full term
                    searchGroup.AddCondition("quotenumber", ConditionOperator.Like, $"%{searchTerm}%");
                }

                // Also search by client (customer) name via the linked account
                searchGroup.AddCondition("customer", "name", ConditionOperator.Like, $"%{searchTerm}%");

                // Combine with status filter
                var combinedFilter = new FilterExpression(LogicalOperator.And);
                combinedFilter.AddFilter(searchGroup);

                // Exclude Draft/Inactive, Won, Lost, Closed quotes.
                combinedFilter.AddCondition("statuscode", ConditionOperator.NotIn, new object[] { 0, 2, 3, 4 });

                // NOTE: We deliberately do NOT add a server-side "isc_projectnumbervisible IS NULL"
                // condition here. For users without field-level-security read access to that column,
                // Dataverse makes the IS NULL predicate match zero rows, which made search return
                // empty for them. The project-number rule is enforced in C# via Quote.IsActive below
                // (see the .Where(q.IsActive) filter), mirroring the fallback already used in GetQuotes.
                query.Criteria = combinedFilter;

                System.Diagnostics.Debug.WriteLine("SearchQuotes: Query filters - (name LIKE OR quotenumber LIKE) AND statuscode NOT IN (0,2,3,4); project-number filtered in C# via IsActive");

                // Page size for search results
                query.PageInfo = new PagingInfo
                {
                    Count = 1000,
                    PageNumber = 1
                };

                System.Diagnostics.Debug.WriteLine("SearchQuotes: Executing query to Dataverse...");
                var result = _connector.ExecuteWithRetry(() => _connector._orgService.RetrieveMultiple(query));

                if (result?.Entities == null)
                {
                    System.Diagnostics.Debug.WriteLine("SearchQuotes: No results returned from Dataverse");
                    return new List<Quote>();
                }

                System.Diagnostics.Debug.WriteLine($"SearchQuotes: Retrieved {result.Entities.Count} raw entities from Dataverse");

                var quotes = result.Entities
                    .Select(Quote.FromEntity)
                    .Where(q => q != null && q.IsActive)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"SearchQuotes: Found {quotes.Count} active quotes for term '{searchTerm}' (from {result.Entities.Count} entities)");

                if (result.Entities.Count > 0 && quotes.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: SearchQuotes retrieved entities but none converted to active quotes");
                }

                System.Diagnostics.Debug.WriteLine("=== SearchQuotes: Completed ===");

                return quotes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SearchQuotes ERROR: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"SearchQuotes STACK TRACE: {ex.StackTrace}");
                throw;
            }
        }

        public Quote GetQuote(Guid id)
        {
            try
            {
                if (!_connector.Connect())
                {
                    return null;
                }

                var columns = new string[]
                {
                    "name",
                    "quotenumber",
                    "customerid",
                    "statuscode",
                    "isc_projectnumbervisible"
                };

                Entity entity = _connector.Retrieve(_entityName, id, columns);
                return Quote.FromEntity(entity);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving quote: {ex.Message}",
                    "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        public string GetQuoteDebugInfo()
        {
            var debugInfo = new System.Text.StringBuilder();
            debugInfo.AppendLine("=== QUOTE LOADING DEBUG REPORT ===");
            debugInfo.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            debugInfo.AppendLine($"Machine: {Environment.MachineName}");
            debugInfo.AppendLine($"User: {Environment.UserName}");
            debugInfo.AppendLine($"OS: {Environment.OSVersion}");
            debugInfo.AppendLine();

            // Impersonation state
            var authService = DHA.DSTC.WPF.Services.DataverseAuthService.Instance;
            var client = authService?.Client;
            debugInfo.AppendLine("0. IMPERSONATION STATE");
            debugInfo.AppendLine($"   ServiceLocator.IsImpersonating: {Utilities.ServiceLocator.IsImpersonating}");
            debugInfo.AppendLine($"   ServiceLocator.ImpersonatedUserName: {Utilities.ServiceLocator.ImpersonatedUserName ?? "(none)"}");
            debugInfo.AppendLine($"   ServiceLocator.ImpersonatedUserId: {Utilities.ServiceLocator.ImpersonatedUserId}");
            debugInfo.AppendLine($"   CrmServiceClient.CallerId: {client?.CallerId}");
            debugInfo.AppendLine($"   _orgService type: {_connector._orgService?.GetType().FullName ?? "null"}");
            debugInfo.AppendLine($"   CrmServiceClient type: {client?.GetType().FullName ?? "null"}");
            debugInfo.AppendLine($"   Same object? {(_connector._orgService != null && ReferenceEquals(_connector._orgService, client))}");
            debugInfo.AppendLine();

            try
            {
                // Test connection
                debugInfo.AppendLine("1. CONNECTION TEST");
                if (!_connector.Connect())
                {
                    debugInfo.AppendLine("   ❌ FAILED to connect to Dataverse");
                    debugInfo.AppendLine("   This is likely the root cause - check network/credentials");
                    return debugInfo.ToString();
                }
                debugInfo.AppendLine("   ✓ Successfully connected to Dataverse");

                // Re-check after Connect() call - did it clobber the service?
                debugInfo.AppendLine($"   _orgService type AFTER Connect(): {_connector._orgService?.GetType().FullName ?? "null"}");
                debugInfo.AppendLine($"   Same object AFTER Connect()? {(_connector._orgService != null && ReferenceEquals(_connector._orgService, client))}");

                // Get current user info from Dataverse
                Guid userId = Guid.Empty;
                Guid businessUnitId = Guid.Empty;
                try
                {
                    var whoAmIRequest = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                    var whoAmIResponse = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)_connector._orgService.Execute(whoAmIRequest);
                    userId = whoAmIResponse.UserId;
                    businessUnitId = whoAmIResponse.BusinessUnitId;

                    debugInfo.AppendLine($"   Connected User ID: {userId}");
                    debugInfo.AppendLine($"   Business Unit ID: {businessUnitId}");

                    // Try to get user name and business unit
                    try
                    {
                        var userEntity = _connector._orgService.Retrieve("systemuser", userId, new ColumnSet("fullname", "domainname", "businessunitid"));
                        var fullName = userEntity.Contains("fullname") ? userEntity["fullname"]?.ToString() : "N/A";
                        var domainName = userEntity.Contains("domainname") ? userEntity["domainname"]?.ToString() : "N/A";
                        debugInfo.AppendLine($"   Connected as: {fullName} ({domainName})");

                        // Get business unit name
                        if (userEntity.Contains("businessunitid"))
                        {
                            var buRef = userEntity.GetAttributeValue<EntityReference>("businessunitid");
                            if (buRef != null)
                            {
                                try
                                {
                                    var buEntity = _connector._orgService.Retrieve("businessunit", buRef.Id, new ColumnSet("name"));
                                    var buName = buEntity.Contains("name") ? buEntity["name"]?.ToString() : "N/A";
                                    debugInfo.AppendLine($"   Business Unit: {buName}");
                                }
                                catch { }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore if we can't get user details
                    }
                }
                catch (Exception ex)
                {
                    debugInfo.AppendLine($"   Warning: Could not retrieve user context - {ex.Message}");
                }
                debugInfo.AppendLine();

                // First, check total quotes accessible to user (no filters)
                debugInfo.AppendLine("2. BASELINE CHECK - Total Quotes Accessible");
                int totalQuotes = 0;
                int matchingBoth = 0;
                EntityCollection baselineResult = null;
                try
                {
                    var baselineQuery = new QueryExpression(_entityName)
                    {
                        ColumnSet = new ColumnSet("quoteid", "statuscode", "isc_projectnumbervisible", "ownerid", "createdby"),
                        PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
                    };
                    baselineResult = _connector._orgService.RetrieveMultiple(baselineQuery);
                    totalQuotes = baselineResult?.Entities?.Count ?? 0;
                    debugInfo.AppendLine($"   Total quotes in system (no filters): {totalQuotes}");

                    if (totalQuotes == 0)
                    {
                        debugInfo.AppendLine("   ⚠️ WARNING: User has NO access to any quotes in the system!");
                        debugInfo.AppendLine("   This suggests a security role or record-level access issue.");
                    }
                    else
                    {
                        // Analyze why quotes are being filtered
                        debugInfo.AppendLine();
                        debugInfo.AppendLine("   Status Code Distribution:");
                        var statusGroups = baselineResult.Entities
                            .GroupBy(e => e.Contains("statuscode") ? e.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? -1 : -1)
                            .OrderBy(g => g.Key)
                            .ToList();

                        foreach (var group in statusGroups)
                        {
                            string statusName;
                            switch (group.Key)
                            {
                                case 0:
                                    statusName = "Draft/Inactive";
                                    break;
                                case 1:
                                    statusName = "Active";
                                    break;
                                case 2:
                                    statusName = "Won";
                                    break;
                                case 3:
                                    statusName = "Lost";
                                    break;
                                case 4:
                                    statusName = "Closed";
                                    break;
                                case -1:
                                    statusName = "Unknown/Missing";
                                    break;
                                default:
                                    statusName = $"Status {group.Key}";
                                    break;
                            }
                            debugInfo.AppendLine($"     {statusName} ({group.Key}): {group.Count()} quotes");
                        }

                        debugInfo.AppendLine();
                        debugInfo.AppendLine("   Project Assignment:");
                        int withProjectNumber = baselineResult.Entities.Count(e =>
                            e.Contains("isc_projectnumbervisible") &&
                            !string.IsNullOrWhiteSpace(e["isc_projectnumbervisible"]?.ToString()));
                        int withoutProjectNumber = totalQuotes - withProjectNumber;
                        debugInfo.AppendLine($"     With project number assigned: {withProjectNumber}");
                        debugInfo.AppendLine($"     Without project number (null): {withoutProjectNumber}");

                        // Show what would pass the filters
                        debugInfo.AppendLine();
                        debugInfo.AppendLine("   Quotes Matching BOTH Criteria:");
                        matchingBoth = baselineResult.Entities.Count(e =>
                        {
                            var statusValue = e.Contains("statuscode") ? e.GetAttributeValue<OptionSetValue>("statuscode")?.Value : (int?)null;
                            var projectVisible = e.Contains("isc_projectnumbervisible") ? e["isc_projectnumbervisible"]?.ToString() : null;

                            bool statusCodeValid = statusValue.HasValue &&
                                                  statusValue.Value != 0 &&
                                                  statusValue.Value != 2 &&
                                                  statusValue.Value != 3 &&
                                                  statusValue.Value != 4;
                            bool projectNumberNull = string.IsNullOrWhiteSpace(projectVisible);

                            return statusCodeValid && projectNumberNull;
                        });
                        debugInfo.AppendLine($"     Status NOT IN (0,2,3,4) AND project number IS NULL: {matchingBoth}");

                        // Check ownership distribution
                        if (userId != Guid.Empty)
                        {
                            debugInfo.AppendLine();
                            debugInfo.AppendLine("   Ownership Analysis:");
                            int ownedByUser = baselineResult.Entities.Count(e =>
                            {
                                if (!e.Contains("ownerid")) return false;
                                var owner = e.GetAttributeValue<EntityReference>("ownerid");
                                return owner?.Id == userId;
                            });
                            int createdByUser = baselineResult.Entities.Count(e =>
                            {
                                if (!e.Contains("createdby")) return false;
                                var creator = e.GetAttributeValue<EntityReference>("createdby");
                                return creator?.Id == userId;
                            });
                            debugInfo.AppendLine($"     Owned by this user: {ownedByUser}");
                            debugInfo.AppendLine($"     Created by this user: {createdByUser}");
                            debugInfo.AppendLine($"     Owned by others: {totalQuotes - ownedByUser}");

                            if (ownedByUser == 0 && totalQuotes > 0)
                            {
                                debugInfo.AppendLine("     ⚠️ User owns NONE of the {totalQuotes} quotes they can see.");
                                debugInfo.AppendLine("     This may indicate record-level security is NOT based on ownership.");
                            }
                        }

                        // Show breakdown by status for quotes WITHOUT project numbers
                        debugInfo.AppendLine();
                        debugInfo.AppendLine("   Detailed Breakdown (Quotes WITHOUT project numbers):");
                        var quotesWithoutProject = baselineResult.Entities.Where(e =>
                        {
                            var projectVisible = e.Contains("isc_projectnumbervisible") ? e["isc_projectnumbervisible"]?.ToString() : null;
                            return string.IsNullOrWhiteSpace(projectVisible);
                        }).ToList();

                        if (quotesWithoutProject.Any())
                        {
                            var statusBreakdown = quotesWithoutProject
                                .GroupBy(e => e.Contains("statuscode") ? e.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? -1 : -1)
                                .OrderBy(g => g.Key)
                                .ToList();

                            foreach (var group in statusBreakdown)
                            {
                                string statusName;
                                switch (group.Key)
                                {
                                    case 0:
                                        statusName = "Draft/Inactive";
                                        break;
                                    case 1:
                                        statusName = "Active";
                                        break;
                                    case 2:
                                        statusName = "Won";
                                        break;
                                    case 3:
                                        statusName = "Lost";
                                        break;
                                    case 4:
                                        statusName = "Closed";
                                        break;
                                    case -1:
                                        statusName = "Unknown/Missing";
                                        break;
                                    default:
                                        statusName = $"Status {group.Key}";
                                        break;
                                }
                                debugInfo.AppendLine($"     {statusName} ({group.Key}): {group.Count()} quotes");
                            }
                        }
                        else
                        {
                            debugInfo.AppendLine("     ⚠️ ALL quotes have project numbers assigned!");
                        }

                        // Sample quotes that SHOULD be visible
                        if (matchingBoth > 0)
                        {
                            debugInfo.AppendLine();
                            debugInfo.AppendLine("   Sample Quotes That SHOULD Be Visible (First 5):");
                            var sampleMatching = baselineResult.Entities.Where(e =>
                            {
                                var statusValue = e.Contains("statuscode") ? e.GetAttributeValue<OptionSetValue>("statuscode")?.Value : (int?)null;
                                var projectVisible = e.Contains("isc_projectnumbervisible") ? e["isc_projectnumbervisible"]?.ToString() : null;

                                bool statusCodeValid = statusValue.HasValue &&
                                                      statusValue.Value != 0 &&
                                                      statusValue.Value != 2 &&
                                                      statusValue.Value != 3 &&
                                                      statusValue.Value != 4;
                                bool projectNumberNull = string.IsNullOrWhiteSpace(projectVisible);

                                return statusCodeValid && projectNumberNull;
                            }).Take(5).ToList();

                            foreach (var entity in sampleMatching)
                            {
                                var quoteName = entity.Contains("name") ? entity["name"]?.ToString() : "N/A";
                                var quoteNumber = entity.Contains("quotenumber") ? entity["quotenumber"]?.ToString() : "N/A";
                                var statusValue = entity.Contains("statuscode") ? entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value : -1;
                                debugInfo.AppendLine($"     - {quoteNumber}: {quoteName} (Status: {statusValue})");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    debugInfo.AppendLine($"   Error checking baseline: {ex.Message}");
                    debugInfo.AppendLine($"   Stack trace: {ex.StackTrace}");
                }
                debugInfo.AppendLine();

                // Additional obscure checks
                if (totalQuotes > 0 && matchingBoth == 0 && baselineResult != null)
                {
                    debugInfo.AppendLine("2b. ADDITIONAL DIAGNOSTIC CHECKS");

                    // Check for data type/encoding issues with isc_projectnumbervisible
                    try
                    {
                        var projectFieldCheck = baselineResult.Entities.Take(10).Select(e =>
                        {
                            if (!e.Contains("isc_projectnumbervisible")) return "MISSING";
                            var value = e["isc_projectnumbervisible"];
                            if (value == null) return "NULL";
                            var strValue = value.ToString();
                            if (string.IsNullOrEmpty(strValue)) return "EMPTY_STRING";
                            if (string.IsNullOrWhiteSpace(strValue)) return $"WHITESPACE_ONLY(len={strValue.Length})";
                            return $"VALUE:{strValue.Substring(0, Math.Min(20, strValue.Length))}";
                        }).ToList();

                        debugInfo.AppendLine("   Field Value Analysis (isc_projectnumbervisible, first 10 quotes):");
                        for (int i = 0; i < projectFieldCheck.Count; i++)
                        {
                            debugInfo.AppendLine($"     Quote {i + 1}: {projectFieldCheck[i]}");
                        }
                    }
                    catch (Exception ex)
                    {
                        debugInfo.AppendLine($"   Field analysis error: {ex.Message}");
                    }

                    // Check for any quotes with status code 1 (Active)
                    try
                    {
                        var activeQuotes = baselineResult.Entities.Where(e =>
                        {
                            var statusValue = e.Contains("statuscode") ? e.GetAttributeValue<OptionSetValue>("statuscode")?.Value : (int?)null;
                            return statusValue == 1;
                        }).ToList();

                        debugInfo.AppendLine();
                        debugInfo.AppendLine($"   Quotes with Status Code 1 (Active): {activeQuotes.Count}");

                        if (activeQuotes.Any())
                        {
                            debugInfo.AppendLine("   First 3 Active quotes:");
                            foreach (var entity in activeQuotes.Take(3))
                            {
                                var quoteName = entity.Contains("name") ? entity["name"]?.ToString() : "N/A";
                                var quoteNumber = entity.Contains("quotenumber") ? entity["quotenumber"]?.ToString() : "N/A";
                                var projectVisible = entity.Contains("isc_projectnumbervisible") ? entity["isc_projectnumbervisible"]?.ToString() ?? "NULL" : "MISSING";
                                debugInfo.AppendLine($"     - {quoteNumber}: {quoteName}, ProjectVisible='{projectVisible}'");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        debugInfo.AppendLine($"   Active quotes check error: {ex.Message}");
                    }

                    debugInfo.AppendLine();
                }

                // Build and execute query
                debugInfo.AppendLine("3. QUERY CONFIGURATION");
                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(
                        "name",
                        "quotenumber",
                        "customerid",
                        "statuscode",
                        "isc_projectnumbervisible"
                    ),
                    Orders = {
                        new OrderExpression("name", OrderType.Ascending)
                    }
                };

                var customerLink = new LinkEntity
                {
                    LinkFromEntityName = "quote",
                    LinkFromAttributeName = "customerid",
                    LinkToEntityName = "account",
                    LinkToAttributeName = "accountid",
                    Columns = new ColumnSet("name"),
                    EntityAlias = "customer",
                    JoinOperator = JoinOperator.LeftOuter
                };
                query.LinkEntities.Add(customerLink);

                var statusFilter = new FilterExpression(LogicalOperator.And);
                statusFilter.AddCondition("statuscode", ConditionOperator.NotIn, new object[] { 0, 2, 3, 4 });

                query.Criteria = statusFilter;

                query.PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1
                };

                debugInfo.AppendLine("   Filters applied:");
                debugInfo.AppendLine("   - statuscode NOT IN (0=Draft, 2=Won, 3=Lost, 4=Closed)");
                debugInfo.AppendLine("   - isc_projectnumbervisible filtering done in C# code after retrieval");
                debugInfo.AppendLine("     (Dataverse NULL checks on this field don't work properly)");
                debugInfo.AppendLine();

                // Test alternate query methods to isolate issue
                if (totalQuotes > 0 && matchingBoth > 0)
                {
                    debugInfo.AppendLine("3b. QUERY VALIDATION TEST");
                    debugInfo.AppendLine("   Expected matches from baseline: " + matchingBoth);

                    try
                    {
                        // Test query with ONLY status filter
                        var testQuery1 = new QueryExpression(_entityName)
                        {
                            ColumnSet = new ColumnSet("quoteid"),
                            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
                        };
                        testQuery1.Criteria.AddCondition("statuscode", ConditionOperator.NotIn, new object[] { 0, 2, 3, 4 });
                        var testResult1 = _connector._orgService.RetrieveMultiple(testQuery1);
                        debugInfo.AppendLine($"   Query with ONLY status filter: {testResult1.Entities.Count} quotes");

                        // Test query with ONLY project number filter
                        var testQuery2 = new QueryExpression(_entityName)
                        {
                            ColumnSet = new ColumnSet("quoteid"),
                            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
                        };
                        testQuery2.Criteria.AddCondition("isc_projectnumbervisible", ConditionOperator.Null);
                        var testResult2 = _connector._orgService.RetrieveMultiple(testQuery2);
                        debugInfo.AppendLine($"   Query with ONLY project number filter: {testResult2.Entities.Count} quotes");

                        debugInfo.AppendLine();
                        debugInfo.AppendLine("   ⚠️ If these individual filters return results but the combined query");
                        debugInfo.AppendLine("   returns 0, there may be a Dataverse query execution issue.");
                    }
                    catch (Exception ex)
                    {
                        debugInfo.AppendLine($"   Query validation error: {ex.Message}");
                    }
                    debugInfo.AppendLine();
                }
                debugInfo.AppendLine();

                // Execute query
                debugInfo.AppendLine("4. QUERY EXECUTION");
                var result = _connector._orgService.RetrieveMultiple(query);

                if (result?.Entities == null)
                {
                    debugInfo.AppendLine("   ❌ No data returned from Dataverse (result.Entities is null)");
                    return debugInfo.ToString();
                }

                var entities = result.Entities.ToList();
                debugInfo.AppendLine($"   ✓ Retrieved {entities.Count} raw entities from Dataverse");
                debugInfo.AppendLine();

                if (entities.Count == 0)
                {
                    debugInfo.AppendLine("5. DIAGNOSIS - NO QUOTES RETURNED BY FILTERED QUERY");
                    debugInfo.AppendLine("   ❌ No quotes match the filter criteria.");
                    debugInfo.AppendLine();

                    // Check if this is a query issue vs data issue
                    if (totalQuotes > 0)
                    {
                        debugInfo.AppendLine("   ROOT CAUSE ANALYSIS:");
                        debugInfo.AppendLine("   The filtered query returned 0 results, but baseline check shows quotes exist.");
                        debugInfo.AppendLine();
                        debugInfo.AppendLine("   This indicates one of the following:");
                        debugInfo.AppendLine("   1. All quotes have invalid status codes (0/Draft, 2/Won, 3/Lost, 4/Closed)");
                        debugInfo.AppendLine("   2. All quotes have project numbers assigned (isc_projectnumbervisible NOT NULL)");
                        debugInfo.AppendLine("   3. The query filter is too restrictive for this user's data");
                        debugInfo.AppendLine();
                        debugInfo.AppendLine("   Check the 'Quotes Matching BOTH Criteria' count in section 2 above.");
                        debugInfo.AppendLine("   If that shows > 0, there may be an issue with the QueryExpression filtering.");
                    }
                    else
                    {
                        debugInfo.AppendLine("   ROOT CAUSE: User has no access to ANY quotes (security/permissions).");
                    }

                    return debugInfo.ToString();
                }

                // Show details of entities
                debugInfo.AppendLine("5. ENTITY DETAILS (First 20)");
                for (int i = 0; i < Math.Min(entities.Count, 20); i++)
                {
                    var entity = entities[i];
                    var quoteName = entity.Contains("name") ? entity["name"]?.ToString() : "N/A";
                    var quoteNumber = entity.Contains("quotenumber") ? entity["quotenumber"]?.ToString() : "N/A";
                    var statusCodeValue = entity.Contains("statuscode") ? entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value : (int?)null;
                    var statusCode = statusCodeValue?.ToString() ?? "N/A";
                    var projectVisible = entity.Contains("isc_projectnumbervisible") ? entity["isc_projectnumbervisible"]?.ToString() : null;

                    debugInfo.AppendLine($"   [{i + 1}] {quoteNumber} - {quoteName}");
                    debugInfo.AppendLine($"       Status Code: {statusCode}");
                    debugInfo.AppendLine($"       Project Visible: {projectVisible ?? "null"}");
                }
                if (entities.Count > 20)
                {
                    debugInfo.AppendLine($"   ... and {entities.Count - 20} more entities");
                }
                debugInfo.AppendLine();

                // Convert and filter
                debugInfo.AppendLine("6. CONVERSION AND FILTERING");
                var quotes = entities
                    .Select(Quote.FromEntity)
                    .Where(q => q != null && q.IsActive)
                    .ToList();

                debugInfo.AppendLine($"   After conversion: {quotes.Count} active quotes (from {entities.Count} entities)");
                debugInfo.AppendLine();

                if (entities.Count > 0 && quotes.Count == 0)
                {
                    debugInfo.AppendLine("7. DIAGNOSIS");
                    debugInfo.AppendLine("   ⚠️ WARNING: Entities were retrieved but NONE converted to active quotes");
                    debugInfo.AppendLine("   This means the entities are being filtered out during conversion.");
                    debugInfo.AppendLine();
                    debugInfo.AppendLine("   Checking why quotes were marked inactive:");

                    int statusCodeIssues = 0;
                    int projectNumberIssues = 0;

                    foreach (var entity in entities.Take(20))
                    {
                        var statusCodeValue = entity.Contains("statuscode") ? entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value : (int?)null;
                        var projectVisible = entity.Contains("isc_projectnumbervisible") ? entity["isc_projectnumbervisible"]?.ToString() : null;

                        bool statusCodeValid = statusCodeValue.HasValue &&
                                              statusCodeValue.Value != 0 &&
                                              statusCodeValue.Value != 2 &&
                                              statusCodeValue.Value != 3 &&
                                              statusCodeValue.Value != 4;
                        bool projectNumberNull = string.IsNullOrWhiteSpace(projectVisible);

                        if (!statusCodeValid) statusCodeIssues++;
                        if (!projectNumberNull) projectNumberIssues++;
                    }

                    debugInfo.AppendLine($"   - {statusCodeIssues} quotes filtered due to invalid status code");
                    debugInfo.AppendLine($"   - {projectNumberIssues} quotes filtered due to project number being set");
                    debugInfo.AppendLine();
                    debugInfo.AppendLine("   ⚠️ THIS IS UNEXPECTED! The database query should have already filtered these out.");
                    debugInfo.AppendLine("   This suggests the query filters may not be working as expected.");
                }
                else if (quotes.Count > 0)
                {
                    debugInfo.AppendLine("7. RESULT");
                    debugInfo.AppendLine($"   ✓ SUCCESS: {quotes.Count} quotes should be displayed");
                    debugInfo.AppendLine();
                    debugInfo.AppendLine("   Sample quotes:");
                    foreach (var quote in quotes.Take(10))
                    {
                        debugInfo.AppendLine($"   - {quote.QuoteNumber}: {quote.Name} (Client: {quote.Client})");
                    }
                }

                return debugInfo.ToString();
            }
            catch (Exception ex)
            {
                debugInfo.AppendLine();
                debugInfo.AppendLine("❌ ERROR OCCURRED:");
                debugInfo.AppendLine($"   Type: {ex.GetType().Name}");
                debugInfo.AppendLine($"   Message: {ex.Message}");
                debugInfo.AppendLine($"   Stack Trace: {ex.StackTrace}");
                return debugInfo.ToString();
            }
        }
    }
}