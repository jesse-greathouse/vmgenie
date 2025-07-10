# 📋 VmGenie Client Module

## 🎯 Overview

VmGenie includes a PowerShell client module (`bin/modules/vmgenie-client.psm1`) that wraps the low-level named pipe protocol with convenient request tracking, response handling, timeouts, and a clean API.

It also provides a `bin/status.ps1` script for CI-friendly binary health checks.

## Location

```text
bin/modules/vmgenie-client.psm1
```

## Features

- ✅ Tracks pending, completed, errored, and timed-out requests
- ✅ Manages newline-terminated JSON framing
- ✅ Handles synchronous transport but allows asynchronous‑style tracking
- ✅ Implements a simple health check (`Test-VmGenieStatus`)

---

## ✨ Health Check

The `Test-VmGenieStatus` cmdlet sends a `status` event to the service and returns:

- `$true` if service responds with `status: ok`
- `$false` otherwise

Example:

```powershell
Import-Module bin\modules\vmgenie-service.psm1 -Force

if (Test-VmGenieStatus) {
    Write-Output "✅ VmGenie Service is up"
} else {
    Write-Output "❌ VmGenie Service is down"
}
```

This is also wrapped in `bin/status.ps1` for convenience:

```powershell
bin\status.ps1
```

Sample output:

```powershell
✅ Service is up
```

or

```powershell
❌ Service is down
```

---

## 📄 Using the Client Module

```powershell
Import-Module bin\modules\vmgenie-client.psm1 -Force

$response = Send-Event -Command "provision" -Parameters @{ template="ubuntu-24.04"; vm_name="devbox" } -Handler {
    param($Response)

    if ($Response.status -eq 'ok') {
        Write-Host "VM provision started: $($Response.data.details)"
    } else {
        Write-Warning "Error: $($Response.data)"
    }

    Complete-Request -Id $Response.id
}
```

---

## 📖 Protocol Recap

- Connects to `\\.\pipe\vmgenie`
- Sends a single JSON line terminated by `\n`
- Reads a single JSON line terminated by `\n`
- Handles one request per connection

---

## 📄 Best Practices

- ✅ Use `Test-VmGenieStatus` in CI or automation
- ✅ Use `Send-Event` with handlers that complete the request
- ✅ Monitor `Get-ErroredRequests` and `Get-TimedOutRequests` if desired
- ✅ Always close the pipe when done (handled by the module)

---

## Debugging

If the client returns no response or times out:

- Check if the service is running (`Test-VmGenieStatus`)
- Review service logs (`bin/log.ps1 -f`)
- Check for stale named pipe connections
