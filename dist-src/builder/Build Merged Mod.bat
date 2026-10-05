@echo off
rem Builds "Elden Vins with more map variations" from your own downloads of Elden Vins Nightreign and
rem More Map Variations 2.1.8-hotfix3 ^& Weapons. Everything is detected automatically; a picker opens only when
rem something cannot be found. Extra options (e.g. --out "D:\Mods\Merged", --no-eldenring) are passed through.
setlocal
cd /d "%~dp0"
"%~dp0NRMerge.exe" build %*
set RC=%ERRORLEVEL%
echo.
pause
exit /b %RC%
