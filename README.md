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
  - [Using VmGenie](#using-vmgenie)
    - [General Syntax](#general-syntax)
    - [Available Commands and Workflows](#available-commands-and-workflows)
      - [1. List VM Instances](#1-list-vm-instances)
      - [2. Provision a New VM](#2-provision-a-new-vm)
      - [3. Start a VM](#3-start-a-vm)
      - [4. Stop a VM](#4-stop-a-vm)
      - [5. Pause and Resume](#5-pause-and-resume)
      - [6. Connect to a VM](#6-connect-to-a-vm)
      - [7. Delete a VM](#7-delete-a-vm)
      - [8. Backup and Restore](#8-backup-and-restore)
      - [9. Clone/Copy a VM](#9-clonecopy-a-vm)
      - [10. Swap/Attach ISO](#10-swapattach-iso)
    - [Command Reference](#command-reference)
    - [Interactive Prompts and Flexibility](#interactive-prompts-and-flexibility)
    - [Getting Help](#getting-help)
    - [Example Workflow](#example-workflow)
  
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

## Using VmGenie

**VmGenie** provides a unified CLI for provisioning, managing, backing up, and connecting to Hyper-V-based virtual machines. Once installed, the `genie` command is available globally in your shell (for your user account), enabling seamless, interactive control of your VM fleet.

### General Syntax

```sh
genie <action> [InstanceName] [options...]
```

- `<action>`: The command to execute (see below for a full list)
- `[InstanceName]`: *(optional)* The name of the VM instance. If omitted, VmGenie prompts you to select or create one.
- `[options...]`: Additional options for the action, if applicable

---

### Available Commands and Workflows

#### 1. List VM Instances

Show all managed VM instances and their status (Running, Off, Paused, etc):

```sh
genie ps
```

#### 2. Provision a New VM

Create and initialize a new VM, interactively (or by specifying all details):

```sh
genie provision
# ...or, with a name and details:
genie provision mylab -Os Ubuntu -Version 24.04
```

If the named artifact does not exist, VmGenie will guide you through creating it.

#### 3. Start a VM

Start an existing VM instance:

```sh
genie start mylab
```

#### 4. Stop a VM

Gracefully stop a running VM:

```sh
genie stop mylab
```

Force stop (power off instantly, like pulling the plug):

```sh
genie stop mylab -Force
```

#### 5. Pause and Resume

Suspend a running VM:

```sh
genie pause mylab
```

Resume a paused VM:

```sh
genie resume mylab
```

#### 6. Connect to a VM

SSH into a running VM (will start it and wait for network readiness if not already running):

```sh
genie connect mylab
```

If you omit the name, you’ll be prompted to select a VM.

#### 7. Delete a VM

Permanently delete a VM and all associated data/artifacts:

```sh
genie delete mylab
```

#### 8. Backup and Restore

**Backup**: Export a VM and all artifacts to a zip archive:

```sh
genie backup mylab
```

**Restore**: Restore a stopped VM from an archive:

```sh
genie restore mylab
# or, specify an archive explicitly:
genie restore -Archive var\export\mylab.zip
```

#### 9. Clone/Copy a VM

Create a new VM instance by cloning an existing one (from a backup archive):

```sh
genie copy mylab -NewInstanceName mylab-copy
# or interactively prompt for the new name:
genie copy mylab
```

#### 10. Swap/Attach ISO

Attach or replace an ISO for a VM:

```sh
genie swap-iso mylab -IsoPath C:\path\to\my.iso
```

---

### Command Reference

| Action      | Description                                                       | Example Command                            |
| ----------- | ----------------------------------------------------------------- | ------------------------------------------ |
| `ps`        | List all VM instances and status                                  | `genie ps`                                 |
| `provision` | Create and initialize a new VM                                    | `genie provision mylab`                    |
| `start`     | Start an existing VM instance                                     | `genie start mylab`                        |
| `stop`      | Gracefully (or force) stop a running VM                           | `genie stop mylab -Force`                  |
| `pause`     | Pause (suspend) a running VM                                      | `genie pause mylab`                        |
| `resume`    | Resume a paused VM                                                | `genie resume mylab`                       |
| `connect`   | Connect to a VM over SSH (starts if needed, waits for networking) | `genie connect mylab`                      |
| `delete`    | Delete a VM and all its data                                      | `genie delete mylab`                       |
| `backup`    | Export VM and artifacts to a zip archive                          | `genie backup mylab`                       |
| `restore`   | Restore VM from a backup archive (VM must be stopped)             | `genie restore mylab`                      |
| `copy`      | Clone a VM from a backup as a new instance                        | `genie copy mylab -NewInstanceName foo`    |
| `swap-iso`  | Attach or swap the ISO for a VM                                   | `genie swap-iso mylab -IsoPath C:\foo.iso` |

---

### Interactive Prompts and Flexibility

- If you omit required arguments (like `[InstanceName]`), **VmGenie will prompt you** with an interactive picker, so you don’t need to remember names.
- Options and flags (e.g., `-Force`, `-NewInstanceName`, `-IsoPath`) can be specified in any order after the action and instance name.

---

### Getting Help

For full help and advanced options on any command, run:

```sh
genie <action> help
```

**Examples:**

```sh
genie provision help
genie backup help
genie swap-iso help
```

---

### Example Workflow

Provision a new VM, start it, connect over SSH, and back it up:

```sh
genie provision mylab -Os Ubuntu -Version 24.04
genie start mylab
genie connect mylab
genie backup mylab
```

---

**Note:**
All VM data, artifacts, and backup archives are managed under the `var\cloud\<instance>` and `var\export\` directories of your `vmgenie` installation unless you specify otherwise.

---

**Advanced:**
See each command’s help (`genie <action> help`) for more on flags, artifact management, and backup/restore details.
