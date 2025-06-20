# Deploy ClickOnce to Azure Static Web Apps
# Requires Azure CLI (install from https://aka.ms/installazurecliwindows)

param(
    [Parameter(Mandatory=$true)]
    [string]$DeploymentToken,
    
    [string]$AppName = "dha-time-management",
    [string]$ProjectFile = "DHA.DSTC.WPF.csproj",
    [string]$Configuration = "Release",
    [string]$CertificatePassword = "YourSecurePassword123!"
)

# Check prerequisites
if (-not (Get-Command "az" -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI not found. Please install from: https://aka.ms/installazurecliwindows"
    exit 1
}

if (-not (Get-Command "msbuild.exe" -ErrorAction SilentlyContinue)) {
    Write-Error "MSBuild not found. Please ensure Visual Studio or Build Tools are installed."
    exit 1
}

# Variables
$tempPublishDir = Join-Path (Get-Location) "publish_temp"
$version = [System.DateTime]::Now.ToString("yyyy.MM.dd.HHmm")
$staticWebAppUrl = "https://$AppName.azurestaticapps.net"

Write-Host "=== DHA Time Management - Static Web Apps Deployment ===" -ForegroundColor Green
Write-Host "Version: $version"
Write-Host "Configuration: $Configuration" 
Write-Host "Static Web App: $staticWebAppUrl"
Write-Host ""

# Step 1: Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path $tempPublishDir) {
    Remove-Item -Path $tempPublishDir -Recurse -Force
}

# Step 2: Update version in project file
Write-Host "Updating version to $version..." -ForegroundColor Yellow
$csprojContent = Get-Content $ProjectFile -Raw
$csprojContent = $csprojContent -replace '<ApplicationVersion>[\d\.]+</ApplicationVersion>', "<ApplicationVersion>$version</ApplicationVersion>"
$csprojContent = $csprojContent -replace '<ApplicationRevision>\d+</ApplicationRevision>', "<ApplicationRevision>0</ApplicationRevision>"
$csprojContent = $csprojContent -replace '<PublishUrl>[^<]+</PublishUrl>', "<PublishUrl>$staticWebAppUrl/</PublishUrl>"
$csprojContent = $csprojContent -replace '<InstallUrl>[^<]+</InstallUrl>', "<InstallUrl>$staticWebAppUrl/</InstallUrl>"
$csprojContent = $csprojContent -replace '<UpdateUrl>[^<]+</UpdateUrl>', "<UpdateUrl>$staticWebAppUrl/</UpdateUrl>"
Set-Content -Path $ProjectFile -Value $csprojContent

# Step 3: Build and publish ClickOnce
Write-Host "Building and publishing ClickOnce application..." -ForegroundColor Yellow
$msbuildArgs = @(
    $ProjectFile
    "/p:Configuration=$Configuration"
    "/p:Platform=AnyCPU"
    "/p:PublishDir=$tempPublishDir\"
    "/p:PublishUrl=$staticWebAppUrl/"
    "/p:InstallUrl=$staticWebAppUrl/"
    "/p:UpdateUrl=$staticWebAppUrl/"
    "/p:ApplicationVersion=$version"
    "/p:CertificatePassword=$CertificatePassword"
    "/t:Publish"
    "/verbosity:minimal"
)

$buildResult = & msbuild @msbuildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

Write-Host "Build completed successfully!" -ForegroundColor Green

# Step 4: Create installation page
Write-Host "Creating installation page..." -ForegroundColor Yellow
$htmlContent = @"
<!DOCTYPE html>
<html lang="en-GB">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>DHA Time Management - Installation</title>
    <link rel="icon" type="image/x-icon" href="/favicon.ico">
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { 
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            line-height: 1.6; 
            color: #1e293b; 
            background: linear-gradient(135deg, #f8fafc 0%, #e2e8f0 100%);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .container { 
            max-width: 600px; 
            margin: 0 auto; 
            padding: 3rem; 
            background: white;
            border-radius: 16px;
            box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1);
            text-align: center;
        }
        .logo { 
            width: 100px; 
            height: auto; 
            margin-bottom: 2rem;
        }
        h1 { 
            font-size: 2.5rem; 
            font-weight: 700; 
            margin-bottom: 1rem;
            background: linear-gradient(135deg, #2563eb, #1d4ed8);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
        }
        .subtitle { 
            font-size: 1.25rem; 
            color: #64748b; 
            margin-bottom: 2rem;
        }
        .install-button { 
            display: inline-block;
            background: linear-gradient(135deg, #2563eb, #1d4ed8);
            color: white; 
            padding: 1rem 2rem; 
            text-decoration: none; 
            border-radius: 12px; 
            font-size: 1.125rem;
            font-weight: 600;
            transition: all 0.3s ease;
            box-shadow: 0 4px 15px rgba(37, 99, 235, 0.4);
            margin-bottom: 2rem;
        }
        .install-button:hover { 
            transform: translateY(-2px);
            box-shadow: 0 8px 25px rgba(37, 99, 235, 0.6);
        }
        .version { 
            color: #64748b; 
            font-size: 0.875rem; 
            margin-top: 1rem;
            padding: 0.75rem;
            background: #f1f5f9;
            border-radius: 8px;
        }
        .requirements {
            text-align: left;
            margin-top: 2rem;
            padding: 1.5rem;
            background: #fef3c7;
            border-radius: 8px;
            border-left: 4px solid #f59e0b;
        }
        .requirements h3 {
            color: #92400e;
            margin-bottom: 0.5rem;
        }
        .requirements ul {
            color: #78350f;
            margin-left: 1.5rem;
        }
        .features {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
            gap: 1rem;
            margin-top: 2rem;
        }
        .feature {
            padding: 1rem;
            background: #f8fafc;
            border-radius: 8px;
            border: 1px solid #e2e8f0;
        }
        .feature-icon {
            font-size: 2rem;
            margin-bottom: 0.5rem;
        }
    </style>
</head>
<body>
    <div class="container">
        <h1>DHA Time Management</h1>
        <p class="subtitle">Professional time tracking and disbursement management</p>
        
        <a href="setup.exe" class="install-button">
            📥 Install Application
        </a>
        
        <div class="features">
            <div class="feature">
                <div class="feature-icon">⏱️</div>
                <h3>Time Tracking</h3>
                <p>Log time entries with project tracking</p>
            </div>
            <div class="feature">
                <div class="feature-icon">💰</div>
                <h3>Disbursements</h3>
                <p>Track expenses and disbursements</p>
            </div>
            <div class="feature">
                <div class="feature-icon">📅</div>
                <h3>Calendar View</h3>
                <p>Visual calendar of your logged hours</p>
            </div>
            <div class="feature">
                <div class="feature-icon">🔄</div>
                <h3>Auto Updates</h3>
                <p>Automatically stays up to date</p>
            </div>
        </div>
        
        <div class="requirements">
            <h3>📋 System Requirements</h3>
            <ul>
                <li>Windows 10 or later</li>
                <li>.NET Framework 4.8 or later</li>
                <li>Internet connection for Dynamics 365 access</li>
                <li>Minimum 100 MB free disk space</li>
            </ul>
        </div>
        
        <div class="version">
            Version: $version<br>
            Released: $(Get-Date -Format "dd MMMM yyyy")<br>
            Architecture: Any CPU (.NET Framework 4.8)
        </div>
    </div>
</body>
</html>
"@

$indexPath = Join-Path $tempPublishDir "index.html"
Set-Content -Path $indexPath -Value $htmlContent

# Step 5: Deploy to Static Web Apps
Write-Host "Deploying to Azure Static Web Apps..." -ForegroundColor Yellow

try {
    # Use Azure CLI to deploy
    $deployCmd = "az staticwebapp deploy --name `"$AppName`" --source `"$tempPublishDir`" --deployment-token `"$DeploymentToken`""
    Invoke-Expression $deployCmd
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "=== DEPLOYMENT COMPLETED SUCCESSFULLY ===" -ForegroundColor Green
        Write-Host ""
        Write-Host "🌐 Installation URL: $staticWebAppUrl" -ForegroundColor Cyan
        Write-Host "📥 Direct download: $staticWebAppUrl/setup.exe" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "📋 To install:" -ForegroundColor White
        Write-Host "1. Navigate to: $staticWebAppUrl"
        Write-Host "2. Click 'Install Application'"
        Write-Host "3. Follow the installation prompts"
        Write-Host ""
        Write-Host "🎉 Version $version deployed successfully!" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "💡 Tip: Bookmark the installation URL for easy access"
    } else {
        Write-Error "Deployment failed. Check the error messages above."
    }
}
catch {
    Write-Error "Deployment failed: $($_.Exception.Message)"
}
finally {
    # Clean up
    Write-Host "Cleaning up temporary files..." -ForegroundColor Yellow
    Remove-Item -Path $tempPublishDir -Recurse -Force -ErrorAction SilentlyContinue
}