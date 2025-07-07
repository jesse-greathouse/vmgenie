param(
    [Parameter(Position=0)]
    [int]$n = 20,

    [Parameter(Position=1)]
    [switch]$f,

    [switch]$Help
)

if ($Help) {
@"
Usage: bin\log.ps1 [n] [-f]

Options:
  n       Show last n log entries (default: 20).
  -f      Follow the log (continuously print new entries, like tail -f).
  -Help   Show this help message.

Examples:
  bin\log.ps1
      Show last 20 entries and exit.

  bin\log.ps1 50
      Show last 50 entries and exit.

  bin\log.ps1 -f
      Show last 20 entries and keep printing as new logs arrive.

  bin\log.ps1 50 -f
      Show last 50 entries and keep printing as new logs arrive.
"@ | Write-Output
    exit 0
}

$source = "VmGenie Service"
$logName = "Application"

function Show-Log {
    param (
        [int]$Lines
    )

    Get-EventLog -LogName $logName -Source $source -Newest $Lines -ErrorAction SilentlyContinue |
        Sort-Object TimeGenerated |
        ForEach-Object {
            Write-Host ("[{0}] {1}: {2}" -f $_.TimeGenerated.ToString("u"), $_.EntryType, $_.Message)
        }
}

function Wait-Log {
    param (
        [int]$Lines
    )

	Write-Host "📡 Following log (Ctrl+C to stop)..."

    # Get initial timestamp of the newest entry
    $lastTime = (Get-Date).AddMinutes(-5) # fallback in case no previous logs

    $initial = Get-EventLog -LogName $logName -Source $source -Newest $Lines -ErrorAction SilentlyContinue |
        Sort-Object TimeGenerated

    if ($initial) {
        $lastTime = $initial[-1].TimeGenerated
        foreach ($entry in $initial) {
            Write-Host ("[{0}] {1}: {2}" -f $entry.TimeGenerated.ToString("u"), $entry.EntryType, $entry.Message)
        }
    }

    while ($true) {
        Start-Sleep -Seconds 2

        $newEntries = Get-EventLog -LogName $logName -Source $source -ErrorAction SilentlyContinue |
            Where-Object { $_.TimeGenerated -gt $lastTime } |
            Sort-Object TimeGenerated

        foreach ($entry in $newEntries) {
            Write-Host ("[{0}] {1}: {2}" -f $entry.TimeGenerated.ToString("u"), $entry.EntryType, $entry.Message)
            $lastTime = $entry.TimeGenerated
        }
    }
}

if ($f) {
    Wait-Log -Lines $n
    exit 0
} else {
    Show-Log -Lines $n
    exit 0
}
