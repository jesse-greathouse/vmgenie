function Add-Key {
<#
.SYNOPSIS
Generates an SSH RSA key pair for a given VM name using ssh-keygen.

.DESCRIPTION
Generates a 4096-bit RSA key pair with no passphrase, placing the keys
in the default OpenSSH key location. The key comment is set to the VM name
for easy identification.

.PARAMETER Name
The name of the VM. This will be used as the key comment to help associate
the key with the VM.
#>

    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $Name
    )

    if (-not (Get-Command ssh-keygen.exe -ErrorAction SilentlyContinue)) {
        throw "❌ OpenSSH 'ssh-keygen.exe' not found on this system. Install it via 'Optional Features' or run: Add-WindowsCapability -Online -Name OpenSSH.Client~~~~0.0.1.0"
    }

    Write-Host "[INFO] Generating SSH key pair for VM: $Name" -ForegroundColor Cyan

    # Build command
    $cmd = @(
        'ssh-keygen.exe',
        '-t', 'rsa',
        '-b', '4096',
        '-C', $Name,
        '-N', ''
    )

    # Run ssh-keygen interactively to let it save in default location
    & $cmd

    if ($LASTEXITCODE -ne 0) {
        throw "❌ ssh-keygen failed with exit code $LASTEXITCODE"
    }

    Write-Host "[OK] SSH key pair generated for VM: $Name" -ForegroundColor Green
}

function Remove-Key {
<#
.SYNOPSIS
Deletes the SSH key pair associated with a given VM name from the default location.

.PARAMETER Name
The name of the VM. Used to find the public key comment and remove the pair.
#>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $Name
    )

    $sshDir = Join-Path $env:USERPROFILE ".ssh"

    Write-Host "[INFO] Searching for SSH key pair for VM: $Name" -ForegroundColor Cyan

    $pubKeys = Get-ChildItem -Path $sshDir -Filter "*.pub" -ErrorAction SilentlyContinue

    $deleted = $false

    foreach ($pubKey in $pubKeys) {
        $content = Get-Content $pubKey.FullName -Raw
        if ($content -match $Name) {
            $base = [System.IO.Path]::ChangeExtension($pubKey.FullName, $null)
            Remove-Item -Path $base -Force -ErrorAction SilentlyContinue
            Remove-Item -Path $pubKey.FullName -Force -ErrorAction SilentlyContinue
            Write-Host "[OK] Deleted key pair: $base and $($pubKey.FullName)" -ForegroundColor Green
            $deleted = $true
        }
    }

    if (-not $deleted) {
        Write-Warning "⚠️ No SSH key pair found for VM: $Name"
    }
}


function Update-Key {
<#
.SYNOPSIS
Rotates the SSH key pair by deleting the old one and creating a new one for the given VM name.

.PARAMETER Name
The name of the VM. Used as the key comment.
#>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $Name
    )

    Write-Host "[INFO] Rotating SSH key pair for VM: $Name" -ForegroundColor Cyan

    Remove-Key -Name $Name
    Add-Key -Name $Name

    Write-Host "[OK] SSH key pair rotated for VM: $Name" -ForegroundColor Green
}

Export-ModuleMember -Function Add-Key, Remove-Key, Update-Key
