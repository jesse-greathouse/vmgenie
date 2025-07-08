# 📡 VmGenie Service Protocol

This document describes the communication protocol between the `VmGenie Service` and its clients.

---

## Overview

The `VmGenie Service` runs as a Windows Service and listens on a **named pipe** for commands from clients.

- **Pipe Name**

```powershell
\\.\pipe\vmgenie
````

- **Transport:** Windows Named Pipe
- **Encoding:** UTF‑8
- **Framing:** Each message is terminated with a single `\n` character.
- **Format:** JSON
- **Mode:** Request/Response; one command per connection.

---

## Connection Workflow

- 1️⃣ Client connects to the pipe
- 2️⃣ Client sends a single JSON request line, terminated with `\n`
- 3️⃣ Server processes the command
- 4️⃣ Server sends a single JSON response line, terminated with `\n`
- 5️⃣ Client disconnects

## Request Message

A request is a **single UTF‑8 encoded JSON object**, sent as one line (terminated by `\n`).

### Example Request

```json
{"id": "abc123", "command": "status", "parameters": {}}
```

Fields:

| Field        | Type     | Description                          |
| ------------ | -------- | ------------------------------------ |
| `id`         | `string` | Unique identifier assigned by client |
| `command`    | `string` | Name of the command (`status`, etc.) |
| `parameters` | `object` | Optional key/value map of arguments  |

---

## Response Message

The server replies with a **single UTF‑8 encoded JSON object**, sent as one line (terminated by `\n`).

### Example Response

```json
{"id": "abc123", "command": "status", "status": "ok", "data": {"details": "Service is running."}}
```

Fields:

| Field     | Type     | Description                       |
| --------- | -------- | --------------------------------- |
| `id`      | `string` | Copied from request               |
| `command` | `string` | Copied from request               |
| `status`  | `string` | `"ok"` or `"error"`               |
| `data`    | `object` | Response payload or error message |

---

## Example Session

### Client sends

```json
{"id": "abc123", "command": "status", "parameters": {}}
```

### Server replies

```json
{"id": "abc123", "command": "status", "status": "ok", "data": {"details": "Service is running."}}
```

---

## PowerShell Client Example

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
Write-Output "Response: $response"

$pipe.Close()
```

---

## Notes

- The server currently handles one client at a time.
- Messages **must fit within 4096 bytes** (the server’s buffer size).
- Both ends must adhere to the newline-terminated JSON line convention.
- Clients are expected to disconnect after receiving the response.

---

## Future Extensions

You can extend the JSON schema with more `command` values and richer `data` payloads.

### Example

#### Request

```json
{"id": "abc456", "command": "provision", "parameters": { "template": "ubuntu-24.04", "vm_name": "devbox" }}
```

#### Response

```json
{"id": "abc456", "command": "provision", "status": "ok", "data": {"details": "VM 'devbox' provisioning started."}}
```

---

## Error Handling

If the server encounters an error, it responds with `status: error` and includes a message in `data`.

```json
{"id": "abc789", "command": "reboot", "status": "error", "data": "Unknown command: reboot"}
```

---

## Versioning

Currently, the protocol is implicit and unversioned. Future versions may introduce a `version` field or negotiate capabilities.
