Import-Module "$PSScriptRoot\vmgenie-import.psm1"
Import-Scriban

function Convert-Template {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)] [string] $TemplatePath,
        [Parameter(Mandatory)] [string] $OutputPath,
        [Parameter(Mandatory)] [hashtable] $Variables
    )

    if (-not (Test-Path $TemplatePath)) {
        throw "Template file not found: $TemplatePath"
    }

    $templateText = Get-Content -Raw -Path $TemplatePath
    $template = [Scriban.Template]::Parse($templateText)

    if ($template.HasErrors) {
        $messages = $template.Messages -join "`n"
        throw "Errors parsing template:`n$messages"
    }

    $dict = [System.Collections.Generic.Dictionary[string,object]]::new()
    foreach ($k in $Variables.Keys) { $dict[$k] = $Variables[$k] }

    $result = $template.Render($dict)

    $outDir = Split-Path -Parent $OutputPath
    if (-not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir | Out-Null
    }

    $result | Set-Content -Path $OutputPath -Encoding utf8
    Write-Host "[OK] Rendered $TemplatePath → $OutputPath" -ForegroundColor Green
}

Export-ModuleMember -Function Convert-Template
