# Dataverse Query Diagnostics Script

## Purpose

This PowerShell script allows you to diagnose George Stow's quote loading issue **without interrupting his work**. You can run it from your own machine while logged in as yourself, and it will impersonate George to test his permissions and query behavior.

## Prerequisites

1. **PowerShell 5.1 or later**
2. **Microsoft.Xrm.Tooling.Connector** NuGet package installed
   - The script will attempt to load this automatically
   - If it fails, install via: `Install-Package Microsoft.CrmSdk.XrmTooling.CoreAssembly`

## How to Run

### Step 1: Open PowerShell as Administrator
```powershell
# Navigate to the project directory
cd "c:\Users\thalverson\Code\DHA - Release Version\DHA - Quotes into disbursements"
```

### Step 2: Run the Script
```powershell
.\DiagnoseDataverseQueries.ps1
```

### Step 3: Follow the Prompts

**Environment Selection:**
```
Select environment:
1. Production (https://dhapd.crm11.dynamics.com)
2. Dev (https://dhapd-dev.crm11.dynamics.com)
Enter choice (1 or 2): 1
```

**Login:**
- A browser window will open for OAuth login
- Log in with **your admin credentials** (not George's)
- The browser will redirect and authentication will complete

**Impersonation:**
```
Do you want to test as a specific user (impersonation)?
NOTE: You'll log in as yourself, but queries will run as the target user
Enter 'y' to test as George Stow, 'n' or press Enter to test as yourself: y

Common users:
  George Stow: George.Stow@dhatransport.co.uk
Enter the user's email address: George.Stow@dhatransport.co.uk
```

### Step 4: Review the Results

The script will run 8 diagnostic tests and display:
- Connection status
- User context (who you're testing as)
- Query results for various filter combinations
- Field-level security checks
- Analysis of the root cause

## What the Tests Check

| Test | What It Does | What Success Means |
|------|-------------|-------------------|
| **Test 1: Baseline** | Count all quotes (no filters) | George can access quotes |
| **Test 2: Status Filter** | Filter by status only | Status filtering works |
| **Test 3: NULL Filter** | Filter where `isc_projectnumbervisible` IS NULL | NULL filter works (or returns 0 if bug) |
| **Test 4: Combined** | Status + NULL filter together | Full query works |
| **Test 5: FLS Check** | Read `isc_projectnumbervisible` field | Field-level security is OK |
| **Test 6: NOT NULL** | Opposite of Test 3 | Confirms NULL filter behavior |
| **Test 7: Empty String** | Check for empty string instead of NULL | Data type validation |
| **Test 8: OR Filter** | NULL OR empty string | Workaround test |

## Interpreting Results

### Scenario A: NULL Filter Bug (Most Likely)
```
Test 1: Baseline (all quotes)                  150 quotes    OK
Test 2: Status filter only                      45 quotes     OK
Test 3: isc_projectnumbervisible IS NULL        0 quotes      BUG FOUND
Test 4: Combined (status + NULL)                0 quotes      BUG FOUND
```

**Root Cause:** Dataverse NULL filter doesn't work for George's security context
**Status:** Already fixed with fallback logic in QuoteService.cs (lines 98-118)
**Action:** App should already handle this automatically

### Scenario B: Security Role Issue
```
Test 1: Baseline (all quotes)                  0 quotes      ❌
```

**Root Cause:** George's security role lacks Read permission on Quote entity
**Action:** Grant "Read Quote" permission to George's security role in Dataverse

### Scenario C: Field-Level Security
```
Test 5: Field-Level Security check             0 visible, 10 missing    FLS ISSUE DETECTED
```

**Root Cause:** Field-level security blocks `isc_projectnumbervisible` field
**Action:** Grant George's security role read access to this field

### Scenario D: Business Unit Restriction
```
Test 1: Baseline (all quotes)                  150 quotes    OK
  Owned by this user: 0
  Owned by others: 150
```

**Root Cause:** George can see quotes but doesn't own any
**Action:** Check if business unit security is configured correctly

## Saving the Report

The script displays results in the console. To save to a file:

```powershell
.\DiagnoseDataverseQueries.ps1 | Tee-Object -FilePath "GeorgeDiagnostics_$(Get-Date -Format 'yyyyMMdd_HHmmss').txt"
```

## Troubleshooting

### "Could not load Dataverse SDK assemblies"
Install the required NuGet packages:
```powershell
Install-Package Microsoft.CrmSdk.XrmTooling.CoreAssembly
Install-Package Microsoft.CrmSdk.CoreAssemblies
```

### "Connection failed"
- Ensure you have internet connectivity
- Check that the Dataverse URL is correct
- Verify your admin account has appropriate permissions
- Try closing all browser windows and running again

### "User not found"
- Verify George's email address is exactly: `George.Stow@dhatransport.co.uk`
- Check that George's account exists in Dataverse
- Ensure George's account is enabled

## Next Steps After Running

1. **Share the output** with the development team
2. **Compare results** between:
   - Your account (working)
   - George's account (not working)
3. **Identify the difference** in permissions/configuration
4. **Apply the fix** in Dataverse admin center

## Notes

- This script is **read-only** - it only queries data, never modifies anything
- Impersonation requires that **you** have the "Act on Behalf of Another User" privilege
- The script uses the same OAuth authentication as the desktop application
- All queries are logged with detailed diagnostics for troubleshooting
