# vmgenie

🧞 Automated, reproducible Hyper‑V VM provisioning made easy.

## Features

- Generate and manage seed ISOs for cloud-init–enabled Linux VMs
- Standardized templates with runtime‑injected variables
- PowerShell–driven workflow for Windows 11 + Hyper‑V
- Supports multiple OSes and versions
- Communicates with a Windows Service over a robust named pipe protocol

## 🧰 Installation

Run `bin/install.ps1` for guided installation.  
It will validate your system, build the service, install it, and write configuration.

## 📡 Named Pipe Service Protocol

Once installed, the `VmGenie Service` runs in the background as a Windows Service and listens on a named pipe called:

```powershell
\\.\pipe\vmgenie
````

This allows PowerShell scripts, tools, or other clients to send commands and receive responses in a structured, reliable way.

---

### Protocol Overview

- ✅ **Encoding:** UTF‑8
- ✅ **Message Format:** JSON text
- ✅ **Framing:** Each message is terminated with a single `\n` (newline character)
- ✅ **Connection Mode:** Synchronous, one command per connection (connect → send → receive → close)

### Example Workflow

#### Client sends

```json
{"id": "abc123", "command": "status", "parameters": {}}
```

(one line of UTF‑8 JSON, terminated by `\n`)

#### Server replies

```json
{"id": "abc123", "command": "status", "status": "ok", "data": {"details": "Service is running."}}
```

(one line of UTF‑8 JSON, terminated by `\n`)

---

### Writing a Client (PowerShell Example)

```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'vmgenie', [System.IO.Pipes.PipeDirection]::InOut)
$pipe.Connect()

$writer = New-Object System.IO.StreamWriter($pipe, [System.Text.Encoding]::UTF8)
$reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)

# Send request
$writer.WriteLine('{ "id": "abc123", "command": "status", "parameters": {} }')
$writer.Flush()

# Read response
$response = $reader.ReadLine()
Write-Output "Response from service: $response"

$pipe.Close()
```

You can adapt this example to send different commands by changing the JSON payload.

---

### Notes

- The service currently handles one client at a time. If another client tries to connect while one is active, it will wait.
- Both request and response **must be single-line UTF‑8 JSON strings terminated by `\n`.**
- Clients are expected to disconnect after receiving the response.

---

### Debugging

To see logs from the service, you can use the included `bin/log.ps1`:

```powershell
bin\log.ps1
```

Or to follow logs live:

```powershell
bin\log.ps1 -f
```

---

### Extending the Protocol

You can extend the JSON schema with more commands and richer responses. For example:

#### Client

```json
{"id": "abc456", "command": "provision", "parameters": { "template": "ubuntu-24.04", "vm_name": "testvm" }}
```

#### Server

```json
{"id": "abc456", "command": "provision", "status": "ok", "data": {"details": "VM 'testvm' provisioning started."}}
```

---

For more details, see: [docs/protocol.md](./docs/protocol.md)
