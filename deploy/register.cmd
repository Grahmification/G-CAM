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
set OUTDIR=%~dp0..\src\GCam.AddIn\bin\%CONFIG%\net48

REM Two assemblies: the add-in itself, and GCam.SolidWorks for the ActiveX control
REM that SOLIDWORKS activates to host the FeatureManager tab. Registering only the
REM first gives you a working toolbar and a silently missing tab.
set ADDIN=%OUTDIR%\GCam.AddIn.dll
set HOST=%OUTDIR%\GCam.SolidWorks.dll

REM Double-clicking does NOT run elevated. If every regasm call reports
REM "Administrator permissions are needed", that is why - right-click and choose
REM "Run as administrator" instead.
net session >nul 2>&1
if errorlevel 1 (
    echo WARNING: not running as administrator - registration will fail.
    echo          Right-click this file and choose "Run as administrator".
    echo.
)

if not exist "%ADDIN%" (
    echo ERROR: not found: %ADDIN%
    echo Build the solution first.
    echo.
    pause
    exit /b 1
)

set FAILED=0

REM --- Purge any previous registration of the add-in's CLSID -------------------
REM regasm adds a version subkey per assembly version and leaves older ones behind.
REM mscoree then activates the HIGHEST version it finds, so a stale entry pointing
REM at a renamed or deleted DLL wins and SOLIDWORKS silently fails to load the
REM add-in - the checkbox un-ticks itself with no error shown.
REM
REM Deleting the CLSID key first makes every registration a clean one. regasm
REM recreates everything it needs immediately below.
set CLSID={DF725BF7-4CEB-4425-8931-A072139DDD01}
reg delete "HKLM\SOFTWARE\Classes\CLSID\%CLSID%" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Classes\Wow6432Node\CLSID\%CLSID%" /f >nul 2>&1

if /i "%~2"=="/u" (
    echo Unregistering %ADDIN%
    "%REGASM%" /unregister "%ADDIN%" || set FAILED=1
    echo Unregistering %HOST%
    "%REGASM%" /unregister "%HOST%" || set FAILED=1
) else (
    echo Registering %HOST%
    "%REGASM%" /codebase "%HOST%" || set FAILED=1
    echo Registering %ADDIN%
    "%REGASM%" /codebase "%ADDIN%" || set FAILED=1
)

echo.
if "%FAILED%"=="1" (
    echo *** FAILED - see the messages above. ***
) else (
    echo Done.
)

REM Keeps the window open when launched by double-click.
pause

endlocal
exit /b %FAILED%