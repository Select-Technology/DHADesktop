@echo off
REM =============================================================
REM  BuildInstaller.bat
REM  Builds the DHA Time Management MSI installer using WiX 3.x.
REM  All paths are auto-detected from this script's location and
REM  from vswhere/WiX install dirs, so the build works regardless
REM  of where the repo lives or which VS/WiX version is present.
REM =============================================================
setlocal EnableExtensions

REM --- Resolve repo paths relative to this script ---
set "INSTALLER_DIR=%~dp0"
if "%INSTALLER_DIR:~-1%"=="\" set "INSTALLER_DIR=%INSTALLER_DIR:~0,-1%"
for %%I in ("%INSTALLER_DIR%") do set "SOLUTION_DIR=%%~dpI"
if "%SOLUTION_DIR:~-1%"=="\" set "SOLUTION_DIR=%SOLUTION_DIR:~0,-1%"
set "APP_PROJECT_DIR=%SOLUTION_DIR%\DHA - Quotes into disbursements"
set "RELEASE_DIR=%APP_PROJECT_DIR%\bin\Release"

REM --- Locate MSBuild via vswhere ---
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "MSBUILD="
if exist "%VSWHERE%" (
    for /f "usebackq delims=" %%M in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%M"
)
if not defined MSBUILD (
    echo ERROR: Could not locate MSBuild.exe via vswhere.
    exit /b 1
)

REM --- Locate WiX toolset (prefer 3.14, fall back to 3.11) ---
set "WIX_BIN="
if exist "%ProgramFiles(x86)%\WiX Toolset v3.14\bin\candle.exe" set "WIX_BIN=%ProgramFiles(x86)%\WiX Toolset v3.14\bin"
if not defined WIX_BIN if exist "%ProgramFiles(x86)%\WiX Toolset v3.11\bin\candle.exe" set "WIX_BIN=%ProgramFiles(x86)%\WiX Toolset v3.11\bin"
if not defined WIX_BIN (
    echo ERROR: WiX Toolset not found. Install WiX 3.x or set WIX_BIN manually.
    exit /b 1
)

echo.
echo ============================================
echo  DHA Time Management - MSI Build
echo ============================================
echo   MSBuild : %MSBUILD%
echo   WiX     : %WIX_BIN%
echo   Source  : %APP_PROJECT_DIR%
echo.

REM --- Pre-clean: this repo lives in OneDrive, which dehydrates build
REM     outputs into read-only "Files On-Demand" placeholders. That makes
REM     MSBuild's incremental-clean/copy steps fail with "access denied".
REM     Clearing the read-only flag first keeps the build reliable. ---
if exist "%RELEASE_DIR%" attrib -r "%RELEASE_DIR%\*" /s /d >nul 2>&1

REM --- Step 1: Build the WPF app in Release mode ---
echo [1/4] Building WPF application (Release)...
"%MSBUILD%" "%APP_PROJECT_DIR%\DHA.DSTC.WPF.csproj" /p:Configuration=Release /v:minimal /t:Build
if ERRORLEVEL 1 (
    echo ERROR: WPF build failed!
    exit /b 1
)
echo       Build succeeded.
echo.

REM --- Ensure WiX output directories exist ---
if not exist "%INSTALLER_DIR%\obj" mkdir "%INSTALLER_DIR%\obj"
if not exist "%INSTALLER_DIR%\Release" mkdir "%INSTALLER_DIR%\Release"

REM --- Step 2: Harvest files from Release directory using heat.exe ---
echo [2/4] Harvesting application files...
"%WIX_BIN%\heat.exe" dir "%RELEASE_DIR%" ^
    -cg AppFiles ^
    -dr INSTALLFOLDER ^
    -srd ^
    -sreg ^
    -sfrag ^
    -ag ^
    -template fragment ^
    -var var.ReleaseDir ^
    -out "%INSTALLER_DIR%\AppFiles.wxs"
if ERRORLEVEL 1 (
    echo ERROR: Heat harvesting failed!
    exit /b 1
)
echo       File harvesting succeeded.
echo.

REM --- Step 3: Compile .wxs to .wixobj ---
echo [3/4] Compiling WiX sources...
"%WIX_BIN%\candle.exe" ^
    -dReleaseDir="%RELEASE_DIR%" ^
    -dAppSourceDir="%APP_PROJECT_DIR%" ^
    -dInstallerDir="%INSTALLER_DIR%" ^
    -out "%INSTALLER_DIR%\obj\\" ^
    "%INSTALLER_DIR%\Product.wxs" ^
    "%INSTALLER_DIR%\AppFiles.wxs"
if ERRORLEVEL 1 (
    echo ERROR: WiX compilation failed!
    exit /b 1
)
echo       Compilation succeeded.
echo.

REM --- Step 4: Link .wixobj to produce .msi ---
echo [4/4] Linking MSI...
"%WIX_BIN%\light.exe" ^
    -ext WixUIExtension ^
    -cultures:en-us ^
    -out "%INSTALLER_DIR%\Release\DHA.TimeManagement.msi" ^
    "%INSTALLER_DIR%\obj\Product.wixobj" ^
    "%INSTALLER_DIR%\obj\AppFiles.wixobj"
if ERRORLEVEL 1 (
    echo ERROR: WiX linking failed!
    exit /b 1
)
echo       MSI created successfully.
echo.

echo ============================================
echo  SUCCESS!
echo  MSI: %INSTALLER_DIR%\Release\DHA.TimeManagement.msi
echo ============================================
echo.
echo Deploy with:  msiexec /i DHA.TimeManagement.msi /quiet
echo.

endlocal
