param (
    [switch]$NonInteractive,
    [switch]$Help,
    [string]$Key
)

if ($Help) {
    @"
Usage: .\bin\configure.ps1 [--NonInteractive] [--Help]

Runs configuration routine to produce .vmgenie-cfg.yml with required environment settings.

Options:
  --NonInteractive   Use defaults or pre-existing values (no prompts)
  --Help             Show this help message

Examples:
  .\bin\configure.ps1
      Run interactive configuration

  .\bin\configure.ps1 --NonInteractive
      Run with defaults, no prompts
"@ | Write-Output
    exit 0
}

try {
    Import-Module "$PSScriptRoot\modules\vmgenie-import.psm1"
    Import-Module "$PSScriptRoot\modules\vmgenie-config.psm1"
    Import-Module "$PSScriptRoot\modules\vmgenie-prompt.psm1"
    Import-Module "$PSScriptRoot\modules\vmgenie-service.psm1"

    # Initialize
    $cfg = Get-Configuration
    if ($null -eq $cfg) { throw "[FATAL] Get-Configuration returned null." }

    # Derived paths
    $appRoot = (Resolve-Path "$PSScriptRoot\..").Path.ToString()
    $binDir = (Join-Path $appRoot 'bin').ToString()
    $srcDir = (Join-Path $appRoot 'src').ToString()
    $etcDir = (Join-Path $appRoot 'etc').ToString()
    $varDir = (Join-Path $appRoot 'var').ToString()
    $tmpDir = (Join-Path $appRoot 'tmp').ToString()
    $logDir = (Join-Path $varDir 'log').ToString()
    $cloudDir = (Join-Path $varDir 'cloud').ToString()
    $gmiDir = (Join-Path $varDir 'gmi').ToString()
    $templateDir = (Join-Path $etcDir 'cloud').ToString()
    $userName = $env:USERNAME
    $defaultLayout = 'us'
    $defaultLocale = 'en_US.UTF-8'
    $defaultTimezone = 'Etc/UTC'
    $defaultCpuCount = 1
    $defaultMemoryMb = 512
    $vmSwitch = ''

    # Defaults as .NET Dictionary
    $defaults = [System.Collections.Generic.Dictionary[string, object]]::new()
    $defaults['USERNAME'] = $userName
    $defaults['VM_SWITCH'] = $vmSwitch
    $defaults['TIMEZONE'] = $defaultTimezone
    $defaults['LOCALE'] = $defaultLocale
    $defaults['LAYOUT'] = $defaultLayout
    $defaults['CPU_COUNT'] = $defaultCpuCount
    $defaults['MEMORY_MB'] = $defaultMemoryMb
    $defaults['LOG_DIR'] = $logDir
    $defaults['CLOUD_DIR'] = $cloudDir
    $defaults['GMI_DIR'] = $gmiDir
    $defaults['TEMPLATE_DIR'] = $templateDir
    $defaults['APPLICATION_DIR'] = $appRoot
    $defaults['BIN'] = $binDir
    $defaults['VAR'] = $varDir
    $defaults['ETC'] = $etcDir
    $defaults['SRC'] = $srcDir
    $defaults['TMP'] = $tmpDir

    # Map of keys -> prompt functions
    $prompts = @{
        'USERNAME'  = 'Invoke-UsernamePrompt'
        'VM_SWITCH' = 'Invoke-VmSwitchPrompt'
        'TIMEZONE'  = 'Invoke-TimezonePrompt'
        'LAYOUT'    = 'Invoke-LayoutPrompt'
        'LOCALE'    = 'Invoke-LocalePrompt'
        'CPU_COUNT' = 'Invoke-CpuCountPrompt'
        'MEMORY_MB' = 'Invoke-MemoryMbPrompt'
    }

    function Get-ConfigValueOrNull {
        param (
            [System.Collections.Generic.Dictionary[string, object]] $cfg,
            [string] $key
        )

        if ($cfg.ContainsKey($key)) {
            return $cfg[$key]
        }
        else {
            return $null
        }
    }

    function Merge-Defaults {
        Write-Verbose "Merging defaults into configuration"
        foreach ($k in $defaults.Keys) {
            if (-not $cfg.ContainsKey($k) -or [string]::IsNullOrWhiteSpace($cfg[$k])) {
                $cfg[$k] = $defaults[$k]
            }
        }
    }

    function Request-UserInput {
        param(
            [string]$Key
        )

        Write-Host ""
        Write-Host "=============================================================" -ForegroundColor Cyan
        Write-Host " Configuring vmgenie (.vmgenie-cfg.yml)" -ForegroundColor Cyan
        Write-Host "=============================================================" -ForegroundColor Cyan
        Write-Host ""

        $keysToPrompt = $null
        if ($Key) {
            if (-not $prompts.ContainsKey($Key)) {
                Write-Error "Unknown configuration key: $Key"
                exit 1
            }
            $keysToPrompt = @($Key)
        }
        else {
            $keysToPrompt = $prompts.Keys
        }

        foreach ($key in $keysToPrompt) {
            $fn = $prompts[$key]
            $currentValue = Get-ConfigValueOrNull $cfg $key

            switch ($key) {
                'VM_SWITCH' {
                    $serviceUp = $false
                    if (Get-Command -Name Test-VmGenieStatus -ErrorAction SilentlyContinue) {
                        $serviceUp = Test-VmGenieStatus
                    }
                    if ($serviceUp) {
                        $result = & $fn -default $currentValue
                        $cfg[$key] = $result.Id
                    }
                    else {
                        $cfg[$key] = ''
                    }
                }
                default {
                    $result = & $fn -value $currentValue
                    $cfg[$key] = [string]$result
                }
            }
        }
    }

    # Main routine
    Merge-Defaults

    if (-not $NonInteractive) {
        if ($Key) {
            Request-UserInput -Key $Key
        }
        else {
            Request-UserInput
        }
    }

    Save-Configuration -Config $cfg

    Write-Host ""
    Write-Host "[DONE] Configuration saved to .vmgenie-cfg.yml" -ForegroundColor Green
    exit 0
}
catch {
    Write-Error "[FATAL] $_"
    exit 1
}
