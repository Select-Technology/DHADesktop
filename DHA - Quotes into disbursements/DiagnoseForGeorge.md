# Diagnostic Report for George Stow - Quote Loading Issue

## Current Status

We've implemented a diagnostic script and fallback logic in the application to investigate why George cannot see quotes.

## What We've Built

### 1. PowerShell Diagnostic Script: `DiagnoseDataverseQueries.ps1`
- Tests 8 different query patterns against Dataverse
- Can impersonate specific users (George) while logged in as admin
- Checks field-level security, NULL filter behavior, and permissions

### 2. Fallback Logic in QuoteService.cs
- Lines 98-118: If NULL filter returns 0 results, automatically retries without the NULL filter
- Filters project numbers in C# code instead of Dataverse query
- Already deployed in the application

### 3. Debug Report in Application
- `QuoteService.GetQuoteDebugInfo()` method provides comprehensive diagnostics
- Can be called from the application to generate a report

## Next Steps to Run with George

### Option A: Run PowerShell Script (Standalone)
```powershell
cd "c:\Users\thalverson\Code\DHA - Release Version\DHA - Quotes into disbursements"
.\DiagnoseDataverseQueries.ps1
```

When prompted:
- Organization URL: `https://[yourorg].crm.dynamics.com`
- Admin Username: `your-admin@dhatransport.co.uk`
- Admin Password: [your password]
- Impersonate? `y`
- User Email: `George.Stow@dhatransport.co.uk`

### Option B: Add Debug Button to Application (Recommended)
Add a debug button in the app that calls:
```csharp
var debugReport = _quoteService.GetQuoteDebugInfo();
MessageBox.Show(debugReport, "Debug Report");
// Or save to file:
File.WriteAllText("GeorgeDebugReport.txt", debugReport);
```

## What the Diagnostics Will Reveal

1. **Connection Status**: Can George connect to Dataverse?
2. **Total Quotes**: How many quotes can George see (no filters)?
3. **Status Distribution**: How many quotes in each status (Draft, Active, Won, Lost, Closed)?
4. **Project Assignment**: How many quotes have project numbers vs NULL?
5. **NULL Filter Bug**: Does the NULL filter return 0 results for George?
6. **Field-Level Security**: Can George read the `isc_projectnumbervisible` field?
7. **Business Unit Access**: What business unit is George in?
8. **Sample Quotes**: Which specific quotes SHOULD George see?

## Expected Issues to Diagnose

### Scenario 1: Security Role Restriction
- George can see 0 quotes total → Security role lacks Read permission

### Scenario 2: Field-Level Security
- George sees quotes, but `isc_projectnumbervisible` field is missing → FLS blocking

### Scenario 3: NULL Filter Bug (Most Likely)
- George sees quotes with baseline query
- NULL filter returns 0 results
- **Solution**: Fallback logic already implemented (lines 98-118)

### Scenario 4: Business Unit Restriction
- George can only see quotes in his business unit
- All quotes belong to different business units

## Current Application Behavior

The application now automatically:
1. Tries query with NULL filter first
2. If 0 results, retries without NULL filter
3. Filters project numbers in C# code
4. Logs all steps to Debug output

George should see quotes unless there's a security/permission issue.
