param (
    [string]$Mask,
    [string]$Path = ".",
    [switch]$Summary,
    [switch]$Hidden,
    [switch]$Help
)

function Show-Help {
    @"
Usage: .\audit.ps1 [OPTIONS]

Recursively search and optionally print file contents for auditing or context extraction.

Examples:
  .\audit.ps1
      Process all files under the current directory

  .\audit.ps1 -Mask "*.cs|*.xaml"
      Process only C# and XAML files

  .\audit.ps1 -Path "./src"
      Start searching in ./src

  .\audit.ps1 -Summary
      Show only file paths, not content

  .\audit.ps1 -Hidden
      Include hidden files (e.g. .env, .gitignore)

Options:
  -Mask         Glob pattern or multiple patterns like "*.ps1|*.cs"
  -Path         Directory to start searching (default: current directory)
  -Summary      Only print file paths, not file contents
  -Hidden       Include hidden files (those starting with '.')
  -Help         Show this help message
"@ | Out-Host
    exit
}

if ($Help) {
    Show-Help
}

# Split mask into an array of patterns (e.g., "*.cs|*.xaml")
# Convert -Mask glob-style input into proper regex strings
$patterns = @()
if ($Mask) {
    $patterns = ($Mask -split '\|') | ForEach-Object {
        $escaped = [Regex]::Escape($_)
        $escaped = $escaped -replace '\\\*', '.*'
        $escaped = $escaped -replace '\\\?', '.'
        return "^$escaped$"  # Add anchors for full filename match
    }
}

# Directory name exclusion (regex)
$excludedDirs = @(
    '\\var\\', '\\obj\\', '\\.vs\\',
    '\\Debug\\', '\\Release\\',
    '\\packages\\', '\\TestResults\\'
)

# File name exclusion (regex or exact match)
$excludedFiles = @(
    '.*\.dll$', '.*\.exe$', '.*\.pdb$', '.*\.cache$', '.*\.pdf',
    '.*\.log$', '.*\.sln$', '.*\.user$', '.*\.suo$', '^\.DS_Store$',
    '.*\.tmp$', '.*\.bak$', '.*\.g\.cs$', '.*\.AssemblyInfo\.cs$'
)

# Helper to test for binary content
function Is-BinaryFile($path) {
    try {
        $bytes = [System.IO.File]::ReadAllBytes($path)
        foreach ($b in $bytes[0..([Math]::Min(1024, $bytes.Length - 1))]) {
            if ($b -eq 0) { return $true }
        }
        return $false
    } catch {
        return $true
    }
}

# Normalize full path
$resolvedPath = Resolve-Path -Path $Path -ErrorAction Stop

Get-ChildItem -Path $resolvedPath -Recurse -File -Force:$Hidden | ForEach-Object {
    $file = $_
    $fullPath = $file.FullName

    # Skip excluded directories
    foreach ($pattern in $excludedDirs) {
        if ($fullPath -match $pattern) { return }
    }

    # Skip hidden files unless requested
    if (-not $Hidden -and ($file.Name -match '^\.' -or $file.Attributes -match "Hidden")) {
        return
    }

    # Skip excluded file patterns
    foreach ($pattern in $excludedFiles) {
        if ($file.Name -match $pattern) { return }
    }

    # Skip binary files
    if (Is-BinaryFile $fullPath) { return }

    # Apply escaped regex masks
    if ($patterns.Count -gt 0) {
        $matched = $false
        foreach ($pattern in $patterns) {
            if ($file.Name -imatch $pattern) {
                $matched = $true
                break
            }
        }
        if (-not $matched) { return }
    }

    if ($Summary) {
        Write-Output $fullPath
    } else {
        Write-Output "--- START $fullPath ---"
        try {
            Get-Content -Path $fullPath -Raw
        } catch {
            Write-Warning "Could not read $($fullPath): $_"
        }
        Write-Output "--- END $fullPath ---`n"
    }
}
