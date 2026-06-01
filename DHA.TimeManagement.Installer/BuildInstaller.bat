@echo off
REM =============================================================
REM  BuildInstaller.bat
REM  Builds the DHA Time Management MSI installer using WiX 3.11
REM =============================================================
setlocal

REM --- Configuration ---
set WIX_BIN=C:\Program Files (x86)\WiX Toolset v3.11\bin
set MSBUILD=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe
set SOLUTION_DIR=C:\Users\thalverson\Code\DHA\DHA - Release Version
set APP_PROJECT_DIR=%SOLUTION_DIR%\DHA - Quotes into disbursements
set INSTALLER_DIR=%SOLUTION_DIR%\DHA.TimeManagement.Installer
set RELEASE_DIR=%APP_PROJECT_DIR%\bin\Release

echo.
echo ============================================
echo  DHA Time Management - MSI Build
echo ============================================
echo.

REM --- Step 1: Build the WPF app in Release mode ---
echo [1/4] Building WPF application (Release)...
"%MSBUILD%" "%APP_PROJECT_DIR%\DHA.DSTC.WPF.csproj" /p:Configuration=Release /v:minimal /t:Build
if ERRORLEVEL 1 (
    echo ERROR: WPF build failed!
    exit /b 1
)
echo       Build succeeded.
echo.

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
