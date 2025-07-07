Import-Module "$PSScriptRoot\vmgenie-import.psm1"
Import-YamlDotNet

function Get-ConfigFile {
<#
.SYNOPSIS
Returns the full path to the configuration YAML file.
#>
    $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
    return Join-Path $repoRoot '.vmgenie-cfg.yml'
}

function Get-Configuration {
    $configFile = Get-ConfigFile
    $cfg = [System.Collections.Generic.Dictionary[string,object]]::new()

    if (Test-Path $configFile) {
        try {
            $yaml = Get-Content -Raw -Path $configFile

            $deserializer = [YamlDotNet.Serialization.DeserializerBuilder]::new().Build()
            $reader = New-Object System.IO.StringReader($yaml)

            $result = $deserializer.Deserialize($reader, `
                        [System.Collections.Generic.Dictionary[string,object]])

            if ($result) {
                $cfg = $result
            }
        }
        catch {
            Write-Warning "⚠️ Failed to load YAML: $_. Using defaults."
            $cfg = [System.Collections.Generic.Dictionary[string,object]]::new()
        }
    }

    if (-not $cfg.ContainsKey('CREATED_AT')) {
        $cfg['CREATED_AT'] = (Get-Date -Format 'yyyy-MM-dd hh:mm:ss tt').ToString()
    }

    if (-not $cfg.ContainsKey('YAML_LIBRARY')) {
        $yamlAsm = [AppDomain]::CurrentDomain.GetAssemblies() |
            Where-Object { $_.GetName().Name -eq 'YamlDotNet' }

        if ($yamlAsm) {
            $yamlVersion = $yamlAsm.GetName().Version.ToString()
            $cfg['YAML_LIBRARY'] = "YamlDotNet $yamlVersion"
        } else {
            $cfg['YAML_LIBRARY'] = "YamlDotNet unknown"
        }
    }

    return $cfg
}

function Save-Configuration {
    param (
        [Parameter(Mandatory)]
        [System.Collections.Generic.Dictionary[string,object]] $Config
    )

    $configFile = Get-ConfigFile

    try {
        $serializer = [YamlDotNet.Serialization.SerializerBuilder]::new().Build()
        $yaml = $serializer.Serialize($Config)

        $outDir = Split-Path -Parent $configFile
        if (-not (Test-Path $outDir)) {
            New-Item -ItemType Directory -Path $outDir | Out-Null
        }

        $yaml | Set-Content -Path $configFile -Encoding utf8
    }
    catch {
        throw "❌ Failed to save configuration: $_"
    }
}

Export-ModuleMember -Function Get-ConfigFile, Get-Configuration, Save-Configuration
