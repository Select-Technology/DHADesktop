# Dataverse Query Diagnostics Script
Write-Host "=== DATAVERSE QUERY DIAGNOSTICS ===" -ForegroundColor Cyan
Write-Host "This script tests query patterns against Dataverse" -ForegroundColor Cyan
Write-Host ""

# Load assemblies
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$packagesPath = Join-Path $scriptPath "packages"

Write-Host "Loading assemblies..." -ForegroundColor Yellow

$dllPaths = @(
    "Microsoft.CrmSdk.CoreAssemblies.9.0.2.59\lib\net462\Microsoft.Xrm.Sdk.dll",
    "Microsoft.CrmSdk.CoreAssemblies.9.0.2.59\lib\net462\Microsoft.Crm.Sdk.Proxy.dll",
    "Microsoft.CrmSdk.XrmTooling.CoreAssembly.9.1.1.65\lib\net462\Microsoft.Xrm.Tooling.Connector.dll",
    "Microsoft.CrmSdk.XrmTooling.CoreAssembly.9.1.1.65\lib\net462\Microsoft.Rest.ClientRuntime.dll",
    "Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll"
)

foreach ($dll in $dllPaths) {
    $fullPath = Join-Path $packagesPath $dll
    if (Test-Path $fullPath) {
        try {
            Add-Type -Path $fullPath -ErrorAction SilentlyContinue
            Write-Host "  Loaded: $dll" -ForegroundColor Gray
        } catch {}
    }
}

Write-Host "Assemblies loaded" -ForegroundColor Green
Write-Host ""

# Environment selection
Write-Host "Select environment:" -ForegroundColor Yellow
Write-Host "1. Production" -ForegroundColor Cyan
Write-Host "2. Dev" -ForegroundColor Cyan
$envChoice = Read-Host "Enter choice"

if ($envChoice -eq "2") {
    $orgUrl = "https://dhapd-dev.crm11.dynamics.com"
} else {
    $orgUrl = "https://dhapd.crm11.dynamics.com"
}

Write-Host "Using: $orgUrl" -ForegroundColor Green
Write-Host ""

# Connect
$clientId = "41cf8aa2-60ea-404e-b549-32491b2060dc"
$connectionString = "AuthType=OAuth;Url=$orgUrl;AppId=$clientId;RedirectUri=http://localhost;LoginPrompt=Auto;"

Write-Host "Connecting..." -ForegroundColor Yellow
$serviceClient = New-Object Microsoft.Xrm.Tooling.Connector.CrmServiceClient($connectionString)

if (-not $serviceClient.IsReady) {
    Write-Host "Connection failed" -ForegroundColor Red
    Read-Host "Press Enter"
    exit
}

Write-Host "Connected successfully" -ForegroundColor Green
Write-Host ""

# Impersonation
Write-Host "Test as specific user?" -ForegroundColor Yellow
$impersonate = Read-Host "Enter y for George Stow"

if ($impersonate -eq 'y') {
    $targetUserEmail = Read-Host "Enter email"
    
    $query = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("systemuser")
    $query.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet("systemuserid", "fullname")
    $query.Criteria.AddCondition("domainname", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $targetUserEmail)
    
    $userResult = $serviceClient.RetrieveMultiple($query)
    
    if ($userResult.Entities.Count -gt 0) {
        $targetUser = $userResult.Entities[0]
        $serviceClient.CallerId = $targetUser.Id
        Write-Host "Impersonating: $($targetUser['fullname'])" -ForegroundColor Cyan
    }
}

Write-Host ""
Write-Host "=== RUNNING TESTS ===" -ForegroundColor Cyan
Write-Host ""

# Test 1: All quotes
Write-Host "Test 1: All quotes..." -ForegroundColor Yellow
$q1 = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("quote")
$q1.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet("quoteid")
$q1.PageInfo = New-Object Microsoft.Xrm.Sdk.Query.PagingInfo
$q1.PageInfo.Count = 5000
$q1.PageInfo.PageNumber = 1
$r1 = $serviceClient.RetrieveMultiple($q1)
Write-Host "  Result: $($r1.Entities.Count) quotes" -ForegroundColor Green

# Test 2: Status filter
Write-Host "Test 2: Status NOT IN 0,2,3,4..." -ForegroundColor Yellow
$q2 = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("quote")
$q2.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet("quoteid")
$q2.Criteria.AddCondition("statuscode", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::NotIn, @(0, 2, 3, 4))
$q2.PageInfo = New-Object Microsoft.Xrm.Sdk.Query.PagingInfo
$q2.PageInfo.Count = 5000
$q2.PageInfo.PageNumber = 1
$r2 = $serviceClient.RetrieveMultiple($q2)
Write-Host "  Result: $($r2.Entities.Count) quotes" -ForegroundColor Green

# Test 3: NULL filter
Write-Host "Test 3: isc_projectnumbervisible IS NULL..." -ForegroundColor Yellow
$q3 = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("quote")
$q3.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet("quoteid")
$q3.Criteria.AddCondition("isc_projectnumbervisible", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Null)
$q3.PageInfo = New-Object Microsoft.Xrm.Sdk.Query.PagingInfo
$q3.PageInfo.Count = 5000
$q3.PageInfo.PageNumber = 1
$r3 = $serviceClient.RetrieveMultiple($q3)
if ($r3.Entities.Count -eq 0) {
    Write-Host "  Result: 0 quotes - BUG FOUND!" -ForegroundColor Red
} else {
    Write-Host "  Result: $($r3.Entities.Count) quotes" -ForegroundColor Green
}

# Test 4: Combined
Write-Host "Test 4: Combined status + NULL..." -ForegroundColor Yellow
$q4 = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("quote")
$q4.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet("quoteid")
$q4.Criteria.AddCondition("statuscode", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::NotIn, @(0, 2, 3, 4))
$q4.Criteria.AddCondition("isc_projectnumbervisible", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Null)
$q4.PageInfo = New-Object Microsoft.Xrm.Sdk.Query.PagingInfo
$q4.PageInfo.Count = 5000
$q4.PageInfo.PageNumber = 1
$r4 = $serviceClient.RetrieveMultiple($q4)
Write-Host "  Result: $($r4.Entities.Count) quotes" -ForegroundColor Green

Write-Host ""
Write-Host "=== ANALYSIS ===" -ForegroundColor Cyan

if ($r3.Entities.Count -eq 0 -and $r1.Entities.Count -gt 0) {
    Write-Host "NULL filter returns 0 - this is the bug!" -ForegroundColor Red
    Write-Host "Fallback in QuoteService.cs should handle this" -ForegroundColor Green
} elseif ($r4.Entities.Count -gt 0) {
    Write-Host "Queries working correctly" -ForegroundColor Green
    Write-Host "Expected $($r4.Entities.Count) quotes" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== SPECIFIC QUOTE DIAGNOSTIC ===" -ForegroundColor Cyan
Write-Host ""

# Ask for quote number to diagnose
$quoteNumber = Read-Host "Enter quote number to diagnose (e.g., Q27557) or press Enter to skip"

if ($quoteNumber -ne "") {
    Write-Host ""
    Write-Host "Diagnosing quote: $quoteNumber" -ForegroundColor Yellow
    Write-Host ""
    
    # Extract numeric part
    $numericPart = $quoteNumber -replace '\D',''
    
    # Query for the specific quote WITHOUT any filters
    $qDiag = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("quote")
    $qDiag.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet("name", "quotenumber", "customerid", "statuscode", "statecode", "isc_projectnumbervisible", "createdon", "modifiedon")
    
    $searchFilter = New-Object Microsoft.Xrm.Sdk.Query.FilterExpression([Microsoft.Xrm.Sdk.Query.LogicalOperator]::Or)
    $searchFilter.AddCondition("quotenumber", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $quoteNumber)
    $searchFilter.AddCondition("quotenumber", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Like, "%$quoteNumber%")
    if ($numericPart -ne "") {
        $searchFilter.AddCondition("quotenumber", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Like, "%$numericPart%")
    }
    
    $qDiag.Criteria = $searchFilter
    
    # Add link to customer
    $customerLink = New-Object Microsoft.Xrm.Sdk.Query.LinkEntity
    $customerLink.LinkFromEntityName = "quote"
    $customerLink.LinkFromAttributeName = "customerid"
    $customerLink.LinkToEntityName = "account"
    $customerLink.LinkToAttributeName = "accountid"
    $customerLink.Columns = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet("name")
    $customerLink.EntityAlias = "customer"
    $customerLink.JoinOperator = [Microsoft.Xrm.Sdk.Query.JoinOperator]::LeftOuter
    $qDiag.LinkEntities.Add($customerLink)
    
    $rDiag = $serviceClient.RetrieveMultiple($qDiag)
    
    if ($rDiag.Entities.Count -eq 0) {
        Write-Host "NO QUOTE FOUND with number '$quoteNumber'" -ForegroundColor Red
        Write-Host "Possible reasons:" -ForegroundColor Yellow
        Write-Host "  - Quote does not exist in Dataverse" -ForegroundColor Gray
        Write-Host "  - Quote number format is different" -ForegroundColor Gray
        Write-Host "  - User does not have access to this quote" -ForegroundColor Gray
    } else {
        foreach ($entity in $rDiag.Entities) {
            Write-Host "========================================" -ForegroundColor Cyan
            Write-Host "QUOTE DETAILS" -ForegroundColor Cyan
            Write-Host "========================================" -ForegroundColor Cyan
            
            $name = if ($entity.Contains("name")) { $entity["name"] } else { "(null)" }
            $number = if ($entity.Contains("quotenumber")) { $entity["quotenumber"] } else { "(null)" }
            $id = $entity.Id
            
            Write-Host "Quote ID:          $id" -ForegroundColor White
            Write-Host "Quote Number:      $number" -ForegroundColor White
            Write-Host "Name:              $name" -ForegroundColor White
            
            # Customer
            $customerName = "(null)"
            if ($entity.Contains("customer.name")) {
                $aliasedValue = $entity["customer.name"]
                if ($aliasedValue -ne $null) {
                    $customerName = $aliasedValue.Value
                }
            } elseif ($entity.Contains("customerid")) {
                $customerRef = $entity["customerid"]
                if ($customerRef -ne $null) {
                    $customerName = $customerRef.Name
                }
            }
            Write-Host "Customer:          $customerName" -ForegroundColor White
            
            # Status codes
            $statusCode = "(null)"
            $statusCodeValue = -1
            if ($entity.Contains("statuscode")) {
                $statusCodeOption = $entity["statuscode"]
                if ($statusCodeOption -ne $null) {
                    $statusCodeValue = $statusCodeOption.Value
                    $statusCode = $statusCodeValue.ToString()
                }
            }
            
            $stateCode = "(null)"
            if ($entity.Contains("statecode")) {
                $stateCodeOption = $entity["statecode"]
                if ($stateCodeOption -ne $null) {
                    $stateCode = $stateCodeOption.Value.ToString()
                }
            }
            
            Write-Host "Status Code:       $statusCode" -ForegroundColor White
            Write-Host "State Code:        $stateCode" -ForegroundColor White
            
            # Status meaning
            $statusMeaning = switch ($statusCodeValue) {
                0 { "Draft/Inactive" }
                1 { "In Progress" }
                2 { "Won" }
                3 { "Lost" }
                4 { "Closed" }
                default { "Unknown ($statusCodeValue)" }
            }
            Write-Host "Status Meaning:    $statusMeaning" -ForegroundColor White
            
            # Project number visible
            $projectVisible = "(attribute not present)"
            if ($entity.Contains("isc_projectnumbervisible")) {
                $pv = $entity["isc_projectnumbervisible"]
                if ($pv -eq $null) {
                    $projectVisible = "(null)"
                } else {
                    $projectVisible = $pv.ToString()
                }
            }
            Write-Host "ProjectNumVisible: $projectVisible" -ForegroundColor White
            
            # Dates
            if ($entity.Contains("createdon")) {
                Write-Host "Created On:        $($entity['createdon'].ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor White
            }
            if ($entity.Contains("modifiedon")) {
                Write-Host "Modified On:       $($entity['modifiedon'].ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor White
            }
            
            Write-Host ""
            Write-Host "========================================" -ForegroundColor Yellow
            Write-Host "FILTER ANALYSIS" -ForegroundColor Yellow
            Write-Host "========================================" -ForegroundColor Yellow
            
            # Analyze filters
            $statusExcluded = ($statusCodeValue -eq 0) -or ($statusCodeValue -eq 2) -or ($statusCodeValue -eq 3) -or ($statusCodeValue -eq 4)
            $hasProjectNumber = $entity.Contains("isc_projectnumbervisible") -and ($entity["isc_projectnumbervisible"] -ne $null) -and ($entity["isc_projectnumbervisible"].ToString() -ne "")
            
            if ($statusExcluded) {
                Write-Host "Status Filter:     EXCLUDED (statuscode $statusCodeValue is in exclusion list [0,2,3,4])" -ForegroundColor Red
            } else {
                Write-Host "Status Filter:     PASS (statuscode $statusCodeValue is NOT excluded)" -ForegroundColor Green
            }
            
            if ($hasProjectNumber) {
                Write-Host "Project Filter:    EXCLUDED (isc_projectnumbervisible is NOT null: '$projectVisible')" -ForegroundColor Red
            } else {
                Write-Host "Project Filter:    PASS (isc_projectnumbervisible is null/empty)" -ForegroundColor Green
            }
            
            Write-Host ""
            Write-Host "========================================" -ForegroundColor Cyan
            if ($statusExcluded -or $hasProjectNumber) {
                Write-Host "VERDICT: Quote is FILTERED OUT and will NOT appear in app" -ForegroundColor Red
                if ($statusExcluded) {
                    Write-Host "  Reason: Status code $statusCodeValue ($statusMeaning) is excluded" -ForegroundColor Yellow
                }
                if ($hasProjectNumber) {
                    Write-Host "  Reason: Project number visible field is not null" -ForegroundColor Yellow
                }
            } else {
                Write-Host "VERDICT: Quote SHOULD appear in the app" -ForegroundColor Green
                Write-Host "  If it's not appearing, check IsActive logic in Quote.FromEntity()" -ForegroundColor Yellow
            }
            Write-Host "========================================" -ForegroundColor Cyan
        }
    }
}

Write-Host ""
Read-Host "Press Enter to exit"
