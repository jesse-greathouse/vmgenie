# 🧰 VmGenie Service

This document describes the architecture and behavior of the **VmGenie Windows Service**, which implements the server-side of the VmGenie protocol.

---

## 🏗️ Architectural Overview

The `VmGenie Service` is an extensible, event‑driven Windows Service that listens for commands over a named pipe and responds with structured JSON events.

---

### 🧪 Architectural Principles

- **Single Responsibility per Component**
  - `Worker`: owns the Windows Service lifecycle, accepts connections, validates requests.
  - `EventHandlerEngine`: maps commands to handlers and dispatches events.
  - `EventHandlers`: implement specific domain logic and send responses.

- **Event‑Driven Asynchrony**
  Commands arrive as JSON events. Each handler can respond immediately or after some processing, and supports streaming, multi‑stage, or delayed responses.

- **Loose Coupling, High Cohesion**
  - Worker knows nothing about handler logic.
  - Handlers are context-aware but transport-agnostic.

- **Explicit Protocol Validation**
  Every request must include `id`, `command`, and `parameters`. Invalid requests get standardized error responses.

- **Extensibility by Convention**
  Adding a new command only requires a new handler class and registering it in `Program.cs`.

---

## 📄 Named Pipe Endpoint

- **Pipe Name:** `\\.\pipe\vmgenie`
- **Transport:** Windows Named Pipe
- **Encoding:** UTF‑8
- **Framing:** Each message ends with a single `\n`.
- **Mode:** Request/Response, one command per connection.

---

### 📖 Protocol Example

#### Client sends

```json
{"id":"abc123","command":"status","parameters":{}}
```

#### Server replies

```json
{"id":"abc123","command":"status","status":"ok","data":{"details":"Service is running."}}
```

---

## 🧩 Extending the Service

### ✨ How to Add a New Event Handler

#### 1️⃣ Implement `IEventHandler`

```csharp
public interface IEventHandler
{
    Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token);
}
```

Example:

```csharp
public class ProvisionHandler : IEventHandler
{
    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        var vmName = evt.Parameters["vm_name"]?.ToString() ?? "default-vm";

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

#### 2️⃣ Register the Handler

In `Program.cs`:

```csharp
var engine = new EventHandlerEngine();
engine.Register("status", new StatusHandler());
engine.Register("provision", new ProvisionHandler());
engine.Freeze();
```

---

#### 3️⃣ Define the Protocol Contract

Specify the request parameters and response shape in your handler.

---

## 🔧 Best Practices

- ✅ Validate parameters thoroughly
- ✅ Use `ctx.SendResponseAsync` for replies
- ✅ Support cancellation via `CancellationToken`
- ✅ Log meaningful progress via `IWorkerContext`
- ✅ Keep handlers independent and self‑contained

---

## 🔍 Debugging

You can view service logs with:

```powershell
bin\log.ps1
```

or live‑follow:

```powershell
bin\log.ps1 -f
```
