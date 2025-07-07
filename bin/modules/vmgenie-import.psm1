function Import-Library {
<#
.SYNOPSIS
Generic loader for a .NET DLL from bin/modules or NuGet.

.PARAMETER AssemblyName
The name of the .NET assembly as it appears in AppDomain.

.PARAMETER DllName
The DLL filename to look for.

.PARAMETER NuGetPackage
The name of the NuGet package.

.PARAMETER TargetFramework
(optional) The Target Framework Moniker (TFM) folder to use in NuGet. Default: netstandard2.0
#>

    param (
        [Parameter(Mandatory)] [string] $AssemblyName,
        [Parameter(Mandatory)] [string] $DllName,
        [Parameter(Mandatory)] [string] $NuGetPackage,
        [string] $TargetFramework = 'netstandard2.0'
    )

    if ([AppDomain]::CurrentDomain.GetAssemblies().Name -contains $AssemblyName) { return }

    $pathsToCheck = @(
        Join-Path $PSScriptRoot $DllName
    )

    $nugetRoot = Join-Path $env:USERPROFILE ".nuget\packages\$NuGetPackage"
    if (Test-Path $nugetRoot) {
        Get-ChildItem -Directory $nugetRoot |
            Sort-Object Name -Descending |
            ForEach-Object {
                Join-Path $_.FullName "lib\$TargetFramework\$DllName"
            } |
            Where-Object { Test-Path $_ } |
            ForEach-Object { $pathsToCheck += $_ }
    }

    $dllPath = $pathsToCheck | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $dllPath) {
        throw "❌ $DllName not found in bin/modules or NuGet cache."
    }

    Add-Type -Path $dllPath -ErrorAction Stop
}

function Import-Scriban {
<#
.SYNOPSIS
Loads Scriban.dll into the current session if not already loaded.
#>
    Import-Library -AssemblyName 'Scriban' -DllName 'Scriban.dll' -NuGetPackage 'scriban'
}

function Import-YamlDotNet {
<#
.SYNOPSIS
Loads YamlDotNet.dll into the current session if not already loaded.
#>
    Import-Library -AssemblyName 'YamlDotNet' -DllName 'YamlDotNet.dll' -NuGetPackage 'yamldotnet'
}

function Import-Sharprompt {
<#
.SYNOPSIS
Loads Sharprompt.dll into the current session if not already loaded.
#>
    Import-Library -AssemblyName 'Sharprompt' -DllName 'Sharprompt.dll' -NuGetPackage 'sharprompt' -TargetFramework 'net8.0'
}

Export-ModuleMember -Function Import-Scriban, Import-YamlDotNet, Import-Sharprompt
