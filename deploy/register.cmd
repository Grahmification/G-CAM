@echo off
REM Registers (or unregisters) the built G-CAM add-in with SOLIDWORKS.
REM Must be run from an ELEVATED prompt - it writes to HKLM.
REM
REM   register.cmd            register the Debug build
REM   register.cmd Release    register the Release build
REM   register.cmd Debug /u   unregister
REM
REM The build already attempts this automatically; this script is for when you
REM built unelevated and got the "regasm failed" warning.

setlocal
set CONFIG=%~1
if "%CONFIG%"=="" set CONFIG=Debug

set REGASM=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe
set TARGET=%~dp0..\src\GCam.AddIn\bin\%CONFIG%\net48\GCam.AddIn.dll

if not exist "%TARGET%" (
    echo ERROR: not found: %TARGET%
    echo Build the solution first.
    exit /b 1
)

if /i "%~2"=="/u" (
    echo Unregistering %TARGET%
    "%REGASM%" /unregister "%TARGET%"
) else (
    echo Registering %TARGET%
    "%REGASM%" /codebase "%TARGET%"
)

endlocal
