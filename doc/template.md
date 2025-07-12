# 📂 VmGenie OS Template Directory

This document describes how the `etc/cloud/` directory in the project acts as the **data store for operating system templates**, and how you can add or manage additional operating systems and their versions.

---

## 🗂️ Overview

VmGenie uses the `etc/cloud/` folder as a **declarative, file‑system‑backed database** of available operating system templates.

Each supported operating system and version is represented by a directory hierarchy and optional `seed-data` files that are mounted or injected during VM provisioning.

There is no external database — the directory structure *is* the source of truth.

---

## 📁 Directory Structure

```text
etc/
└── cloud/
    ├── Debian/
    │   └── 12/
    │       └── seed-data/
    │           ├── meta-data
    │           └── user-data
    ├── Fedora/
    │   ├── 40/
    │   │   └── seed-data/
    │   └── 42/
    │       └── seed-data/
    ├── OpenSUSE/
    │   ├── 15.6/
    │   │   └── seed-data/
    │   └── Tumbleweed/
    │       └── seed-data/
    └── Ubuntu/
        └── 24.04/
            └── seed-data/
```

### Components

- ✅ **Operating System Name** — top-level directory (e.g., `Ubuntu`, `Fedora`, `OpenSUSE`)
- ✅ **Version Name** — second-level directory (e.g., `24.04`, `40`, `Tumbleweed`)
- ✅ **Seed Data** — optional cloud‑init compatible files under `seed-data/`:
  - `meta-data`
  - `user-data`

---

## 🚀 How it Works

When the user runs the PowerShell prompts:

- 1️⃣ The service lists all directories under `etc/cloud/` to determine available operating systems.
- 2️⃣ When the user selects an OS, the service lists all subdirectories under that OS to determine available versions.
- 3️⃣ When provisioning, if `seed-data` files exist for the selected OS/version, they are used as part of the cloud‑init or ISO provisioning process.

---

## ➕ Adding a New Operating System

To add a new OS template:

### Example: adding `Rocky Linux 9`

1️⃣ Create the OS folder if it does not exist:

```powershell
mkdir etc\cloud\Rocky
```

2️⃣ Create a folder for the desired version:

```powershell
mkdir etc\cloud\Rocky\9
```

3️⃣ Optionally, add `seed-data`:

```powershell
mkdir etc\cloud\Rocky\9\seed-data
notepad etc\cloud\Rocky\9\seed-data\meta-data
notepad etc\cloud\Rocky\9\seed-data\user-data
```

`meta-data` and `user-data` can be standard [cloud‑init](https://cloudinit.readthedocs.io/) files.

---

## 📝 Best Practices

- ✅ Use lowercase or standard names for OS and versions, but they can match upstream conventions (`Tumbleweed`, `24.04`, etc.).
- ✅ Keep `seed-data` files valid YAML or text per cloud‑init specs.
- ✅ Avoid spaces in directory names to keep things consistent.
- ✅ Remove obsolete OS/version folders to prevent them appearing in the prompts.
- ✅ Commit new templates into version control so they’re available to others.

---

## 🔍 Verifying Your Changes

After adding a new OS/version, you can check:

```powershell
Import-Module .\bin\modules\vmgenie-prompt.psm1
$os = Invoke-OperatingSystemPrompt
$version = Invoke-OsVersionPrompt -OperatingSystem $os
```

The newly added OS/version should appear in the list.

---

## 📄 Summary Table

| Level     | Path Example                        | Description                          |
| --------- | ----------------------------------- | ------------------------------------ |
| OS        | `etc/cloud/Ubuntu/`                 | Defines the operating system         |
| Version   | `etc/cloud/Ubuntu/24.04/`           | Defines a specific version           |
| Seed Data | `etc/cloud/Ubuntu/24.04/seed-data/` | Contains `meta-data` and `user-data` |

## 📦 Product Artifact Directory Structure

When you provision a new VM instance using a selected operating system and version template, VmGenie produces a **product directory** under `var/cloud/` named after the instance you specify (e.g., `foo` or `bar`).

This directory contains everything needed to spawn and manage that specific VM instance, including rendered templates, keys, and metadata.

---

### 📁 Example: `var/cloud/foo/`

```text
var/cloud/
└── foo/
    ├── metadata.yml
    ├── seed.iso
    ├── seed-data/
    │   ├── meta-data
    │   └── user-data
    ├── foo.pem
    └── foo.pub
```

#### Artifact Components

| File/Directory        | Description                                                                                                                                                                        |
| --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `metadata.yml`        | Rendered YAML metadata describing the instance, rendered from `etc/metadata.yml` with placeholders replaced (e.g., `{{ OPERATING_SYSTEM }}`, `{{ OS_VERSION }}`, `{{ BASE_VM }}`). |
| `seed.iso`            | Cloud‑init ISO image generated from the `seed-data/` files.                                                                                                                        |
| `seed-data/meta-data` | Rendered meta‑data file (template from `etc/cloud/<os>/<version>/seed-data/meta-data`).                                                                                            |
| `seed-data/user-data` | Rendered user‑data file (template from `etc/cloud/<os>/<version>/seed-data/user-data`).                                                                                            |
| `foo.pem`             | Private SSH key for this instance.                                                                                                                                                 |
| `foo.pub`             | Public SSH key for this instance.                                                                                                                                                  |

---

### 🚀 How the Artifacts Work

When you invoke the provisioning flow (e.g., via `bin/make.ps1` or the API):

- 1️⃣ The selected OS and version determine which `seed-data` templates from `etc/cloud/` to use.
- 2️⃣ Placeholders like `{{ USERNAME }}`, `{{ TIMEZONE }}`, `{{ OPERATING_SYSTEM }}`, etc., are substituted based on user input and config.
- 3️⃣ The rendered `meta-data` and `user-data` are written into `var/cloud/<instance>/seed-data/`.
- 4️⃣ The `metadata.yml` is rendered from `etc/metadata.yml` and saved at the root of the instance directory.
- 5️⃣ An SSH keypair (`<instance>.pem`, `<instance>.pub`) is generated if it doesn’t already exist.
- 6️⃣ A `seed.iso` is generated from the `seed-data/` folder using a tool like `genisoimage` or equivalent.

---

### 📝 Notes

- ✅ Each instance has its own isolated product directory under `var/cloud/`.
- ✅ Keys and cloud‑init files are unique per instance.
- ✅ `metadata.yml` serves as a machine‑readable manifest of the instance configuration.
- ✅ You can safely delete an instance by removing its folder under `var/cloud/`.

---

### 📄 Artifact Summary Table

| Level         | Path Example                     | Description                          |
| ------------- | -------------------------------- | ------------------------------------ |
| Instance Root | `var/cloud/foo/`                 | Instance‑specific folder             |
| Metadata      | `var/cloud/foo/metadata.yml`     | Rendered manifest                    |
| Seed ISO      | `var/cloud/foo/seed.iso`         | Cloud‑init ISO                       |
| Seed Data     | `var/cloud/foo/seed-data/`       | Rendered `meta-data` and `user-data` |
| SSH Keys      | `var/cloud/foo/foo.pem` + `.pub` | SSH key pair                         |
