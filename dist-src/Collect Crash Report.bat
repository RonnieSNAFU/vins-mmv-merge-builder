@echo off
rem Collects logs for a bug report into a zip on your Desktop (nothing is uploaded). See Collect-CrashReport.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Collect-CrashReport.ps1"
