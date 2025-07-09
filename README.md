# vmgenie

🧞 Automated, reproducible Hyper‑V VM provisioning made easy.

I have been asked by Microsoft to demonstrate the architecture for a Virtual Machine provisioning tool, with [cloud-init](https://cloud-init.io/), for the Hyper-V virtual machine hypervisor, for the Windows Operating System.

---

> 📝 **Why vmgenie?**
>
> Hyper‑V, while a powerful virtualization platform on Windows, lacks a native equivalent to *cloud‑init* — the de facto standard for automated provisioning in cloud environments.
> This gap forces users to rely on ad‑hoc scripts, manual VM customization, or brittle templates, making Hyper‑V less convenient for modern, automated workflows.
> `vmgenie` was created to fill that gap: it brings a **cloud‑init–style experience to Hyper‑V**, enabling reproducible, automated provisioning of Linux (and potentially Windows) VMs through declarative configuration, seed ISOs, and a robust Windows Service API.
> The vision is to make Hyper‑V as approachable for automated, large‑scale, or developer‑friendly workflows as Azure or other cloud providers — while remaining lightweight, unintrusive, and secure.

---

## Features

- Generate and manage seed ISOs for cloud-init–enabled Linux VMs
- Standardized templates with runtime‑injected variables
- PowerShell–driven workflow for Windows 11 + Hyper‑V
- Supports multiple OSes and versions
- Communicates with a Windows Service over a robust named pipe protocol

## 🧰 Installation

Run `bin/install.ps1` for guided installation.  
It will validate your system, build the service, install it, and write configuration.

Here is the proposed section to insert into your `README.md`:

---

## 🏗️ VM Genie Service Architectural Thesis

The `VmGenie Service` is designed as a robust, extensible, event‑driven Windows Service.

### 🧪 Architectural Principles

- **Single Responsibility per Component**
  Each part of the system has a clear, narrowly‑focused role:

  - `Worker`: owns the Windows Service lifecycle, handles connections and validates requests.
  - `EventHandlerEngine`: maps commands to handlers and dispatches events.
  - `EventHandlers`: implement domain logic for a specific command and send their own response(s).

- **Event‑Driven Asynchrony**
  Commands are expressed as JSON "events," handled by pluggable handlers. Each handler can respond asynchronously, allowing for streaming, delayed, or multi‑stage responses if needed.

- **Loose Coupling, High Cohesion**
  The `Worker` never needs to know how a handler works — it just validates and routes. Handlers never care how the connection was established — they use the provided context to send responses.

- **Explicit Protocol Validation**
  Incoming requests are rigorously validated against the protocol: must contain `id`, `command`, and `parameters`. Invalid requests receive standardized error responses.

- **Extensibility by Convention**
  New commands can be introduced simply by implementing a new handler and registering it in `Program.cs`. No changes to `Worker` are required.

### 📖 Extending the Service

You can extend `vmgenie` with new functionality by defining and registering additional event handlers. Below is a concise guide.

---

### ✨ How to Add a New Event Handler

#### 1️⃣ Implement `IEventHandler`

Each handler must implement:

```csharp
public interface IEventHandler
{
    Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token);
}
```

For example:

```csharp
using System.Threading;
using System.Threading.Tasks;
using VmGenie;

public class ProvisionHandler : IEventHandler
{
    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        var vmName = evt.Parameters["vm_name"]?.ToString() ?? "default-vm";

        // Perform actual provisioning work here…

        var response = new EventResponse(
            evt.Id,
            evt.Command,
            EventStatus.OK,
            new { details = $"Provisioning VM '{vmName}'" }
        );

        await ctx.SendResponseAsync(response, token);
    }
}
```

---

#### 2️⃣ Register the Handler in `Program.cs`

Add it to the `EventHandlerEngine` before freezing:

```csharp
var engine = new EventHandlerEngine();
engine.Register("status", new StatusHandler());
engine.Register("provision", new ProvisionHandler());
engine.Freeze();
```

---

#### 3️⃣ Define the Protocol Contract

Decide what parameters your command expects and document it. For example:

```json
{
  "id": "xyz789",
  "command": "provision",
  "parameters": {
    "template": "ubuntu-24.04",
    "vm_name": "testvm"
  }
}
```

And a sample response:

```json
{
  "id": "xyz789",
  "command": "provision",
  "status": "ok",
  "data": {
    "details": "Provisioning VM 'testvm'"
  }
}
```

---

### 🔧 Best Practices for Handlers

✅ Validate your parameters thoroughly before proceeding.
✅ Use `ctx.SendResponseAsync` for all replies — the worker will not send them for you.
✅ Be prepared to handle cancellation via the `CancellationToken`.
✅ Log meaningful messages via the `IWorkerContext` or additional dependencies if needed.
✅ Keep handlers independent and self‑contained.

The architecture of `vmgenie` is highly modular and easy to extend.

---

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

For more details, see: [doc/protocol.md](./doc/protocol.md)
