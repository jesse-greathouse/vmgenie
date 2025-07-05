# vmgenie-template.psm1
# Load Scriban from NuGet or local copy
function Import-Scriban {
    if ([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Scriban' }) {
        return
    }

    $scribanDll = $null

    $local = Join-Path $PSScriptRoot 'Scriban.dll'
    if (Test-Path $local) {
        $scribanDll = $local
    } else {
        $nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages\scriban'
        if (Test-Path $nugetRoot) {
            $versions = Get-ChildItem -Directory $nugetRoot | Sort-Object Name -Descending
            foreach ($v in $versions) {
                $candidate = Join-Path $v.FullName 'lib\netstandard2.0\Scriban.dll'
                if (Test-Path $candidate) {
                    $scribanDll = $candidate
                    break
                }
            }
        }
    }

    if (-not $scribanDll) {
        Write-Error '❌ Scriban.dll not found in bin/modules or NuGet cache.'
        exit 1
    }

    Add-Type -Path $scribanDll -ErrorAction Stop
}

Import-Scriban

function Convert-Template {
<#
.SYNOPSIS
Render a Scriban template with a set of variables.
#>

    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $TemplatePath,

        [Parameter(Mandatory)]
        [string] $OutputPath,

        [Parameter(Mandatory)]
        [hashtable] $Variables
    )

    if (-not (Test-Path $TemplatePath)) {
        Write-Error "Template file not found: $TemplatePath"
        exit 1
    }

    $templateText = Get-Content -Raw -Path $TemplatePath

    $template = [Scriban.Template]::Parse($templateText)

    if ($template.HasErrors) {
        Write-Error "Errors parsing template:"
        $template.Messages | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        exit 1
    }

    $dict = [System.Collections.Generic.Dictionary[string,object]]::new()
    foreach ($k in $Variables.Keys) {
        $dict[$k] = $Variables[$k]
    }

    $result = $template.Render($dict)

    $outDir = Split-Path -Parent $OutputPath
    if (-not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir | Out-Null
    }

    $result | Set-Content -Path $OutputPath -Encoding utf8

    Write-Host "[OK] Rendered $TemplatePath → $OutputPath" -ForegroundColor Green
}

Export-ModuleMember -Function Convert-Template
