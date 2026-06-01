@echo off
REM Diagnostic Script Runner for George Stow's Quote Issue
REM Double-click this file to run the diagnostics

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& '%~dp0DiagnoseDataverseQueries.ps1'"

pause
