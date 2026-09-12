<#
.SYNOPSIS
    Diagnoses why SOLIDWORKS refuses to load the G-CAM add-in.

.DESCRIPTION
    When the add-in checkbox in Tools > Add-Ins un-ticks itself immediately,
    SOLIDWORKS shows no error at all. It has tried to CoCreate the add-in's CLSID,
    got a failure, and given up silently.

    This script reproduces that activation outside SOLIDWORKS and reports what
    actually went wrong. Run it from an ordinary (unelevated) prompt:

        powershell -ExecutionPolicy Bypass -File tools\diagnose-addin.ps1

    The usual causes, in rough order of likelihood:

      * A stale version subkey under the CLSID pointing at a renamed or deleted
        DLL. mscoree activates the HIGHEST version it finds, so an old entry beats
        the current one. Fix: deploy\register.cmd, which purges the key first.
      * The add-in was never registered, or was registered unelevated and failed.
      * A dependency the CLR cannot locate.
#>

$ErrorActionPreference = 'Continue'

# Must match the [Guid] on GCamAddin in GCamAddinRegistration.cs.
$Clsid = '{DF725BF7-4CEB-4425-8931-A072139DDD01}'

function Write-Head($text) {
    Write-Host ''
    Write-Host "=== $text ===" -ForegroundColor Cyan
}

Write-Head 'SOLIDWORKS add-in entry'
$addinKey = "HKLM:\SOFTWARE\SolidWorks\Addins\$Clsid"
if (Test-Path $addinKey) {
    $p = Get-ItemProperty $addinKey
    Write-Host "  Title       : $($p.Title)"
    Write-Host "  Description : $($p.Description)"
    Write-Host "  Load at startup (0=off, 1=on): $($p.'(default)')"
} else {
    Write-Host "  MISSING - the add-in is not registered at all." -ForegroundColor Red
    Write-Host "  Run deploy\register.cmd as administrator."
}

Write-Head 'COM class registration'
$problems = @()
$base = "HKLM:\SOFTWARE\Classes\CLSID\$Clsid"
if (-not (Test-Path $base)) {
    Write-Host '  MISSING - regasm has not registered this CLSID.' -ForegroundColor Red
    $problems += 'CLSID not registered'
} else {
    # The default key plus every version subkey. Extra version subkeys are the
    # classic cause: they accumulate as the assembly version changes.
    $keys = @(Get-Item "$base\InprocServer32" -ErrorAction SilentlyContinue)
    $keys += Get-ChildItem "$base\InprocServer32" -ErrorAction SilentlyContinue

    foreach ($k in $keys) {
        if (-not $k) { continue }
        $p = Get-ItemProperty $k.PSPath
        $label = $k.PSChildName
        Write-Host "  [$label]"
        Write-Host "      Class    : $($p.Class)"
        Write-Host "      Assembly : $($p.Assembly)"
        if ($p.CodeBase) {
            $file = ([Uri]$p.CodeBase).LocalPath
            $ok = Test-Path $file
            $colour = if ($ok) { 'Gray' } else { 'Red' }
            Write-Host "      CodeBase : $file" -ForegroundColor $colour
            if (-not $ok) {
                Write-Host '      ^^ THIS FILE DOES NOT EXIST' -ForegroundColor Red
                $problems += "stale registration '$label' points at a missing file"
            }
        }
    }

    $versionKeys = @(Get-ChildItem "$base\InprocServer32" -ErrorAction SilentlyContinue)
    if ($versionKeys.Count -gt 1) {
        Write-Host ''
        Write-Host "  NOTE: $($versionKeys.Count) version subkeys present. mscoree activates the" -ForegroundColor Yellow
        Write-Host '        highest version, which may not be the one you just built.' -ForegroundColor Yellow
        $problems += 'multiple version subkeys'
    }
}

Write-Head 'Activation (what SOLIDWORKS does)'
try {
    $type = [Type]::GetTypeFromCLSID([Guid]$Clsid, $true)
    $obj = [Activator]::CreateInstance($type)
    Write-Host "  OK - activated $($obj.GetType().FullName)" -ForegroundColor Green
    Write-Host "  from $($obj.GetType().Assembly.Location)"
    [Runtime.InteropServices.Marshal]::ReleaseComObject($obj) | Out-Null
} catch {
    Write-Host '  FAILED' -ForegroundColor Red
    $e = $_.Exception
    while ($e) {
        Write-Host "    [$($e.GetType().Name)] $(($e.Message -split "`n")[0])"
        $e = $e.InnerException
    }
    $problems += 'activation failed'
}

Write-Head 'Summary'
if ($problems.Count -eq 0) {
    Write-Host '  No problems found. If SOLIDWORKS still will not load the add-in,' -ForegroundColor Green
    Write-Host '  the failure is inside ConnectToSW - attach Visual Studio to SLDWORKS.exe'
    Write-Host '  (Debug > Attach to Process), enable Common Language Runtime exceptions in'
    Write-Host '  Debug > Windows > Exception Settings, and tick the add-in again.'
} else {
    foreach ($p in $problems) { Write-Host "  * $p" -ForegroundColor Red }
    Write-Host ''
    Write-Host '  Fix: run deploy\register.cmd as administrator. It deletes the CLSID key'
    Write-Host '  before registering, which clears stale entries.'
}
Write-Host ''
