# vmgenie

🧞 Automated, reproducible Hyper‑V VM provisioning made easy.

A demonstration of a modern Virtual Machine provisioning tool for Hyper‑V on Windows, with [cloud-init](https://cloud-init.io)–style declarative workflows and a robust Windows Service backend.

---

> 📝 **Why vmgenie?**
>
> Hyper‑V, while a powerful virtualization platform, lacks a native equivalent to *cloud‑init*.  
> `vmgenie` fills that gap: bringing a **cloud‑init–style experience to Hyper‑V**, enabling reproducible, automated provisioning of Linux (and potentially Windows) VMs through declarative configuration, seed ISOs, and a Windows Service API.

---

## ✨ Features

- Generate and manage seed ISOs for cloud-init–enabled Linux VMs
- Standardized templates with runtime‑injected variables
- PowerShell–driven workflow for Windows 11 + Hyper‑V
- Supports multiple OSes and versions
- Communicates with a Windows Service over a robust named pipe protocol

---

## 📚 Table of Contents

- [vmgenie](#vmgenie)
  - [✨ Features](#-features)
  - [📚 Table of Contents](#-table-of-contents)
  - [📖 Further Documentation](#-further-documentation)
  - [🧰 Installation](#-installation)
  
## 📖 Further Documentation

- [📡 Protocol Specification](doc/protocol.md)
- [🛠  Service Architecture & Extensibility](doc/service.md)
- [📋 Client Module & Automation](doc/client.md)
- [🧩 OS Templating](doc/template.md)
- [✅ Base VM Checklist](doc/base-vm-checklist.md)

---

## 🧰 Installation

Run the installer script:

```powershell
bin/install.ps1
````

This will:

- ✅ Validate your system
- ✅ Build the service
- ✅ Install and start the Windows Service
- ✅ Write configuration

You can then query the service with:

```powershell
bin/status.ps1
```

Sample output:

```powershell
✅ Service is up
```

or

```powershell
❌ Service is down
```
