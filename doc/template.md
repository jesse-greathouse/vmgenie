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

✅ **Operating System Name** — top-level directory (e.g., `Ubuntu`, `Fedora`, `OpenSUSE`)
✅ **Version Name** — second-level directory (e.g., `24.04`, `40`, `Tumbleweed`)
✅ **Seed Data** — optional cloud‑init compatible files under `seed-data/`:

* `meta-data`
* `user-data`

---

## 🚀 How it Works

When the user runs the PowerShell prompts:

1️⃣ The service lists all directories under `etc/cloud/` to determine available operating systems.
2️⃣ When the user selects an OS, the service lists all subdirectories under that OS to determine available versions.
3️⃣ When provisioning, if `seed-data` files exist for the selected OS/version, they are used as part of the cloud‑init or ISO provisioning process.

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

* ✅ Use lowercase or standard names for OS and versions, but they can match upstream conventions (`Tumbleweed`, `24.04`, etc.).
* ✅ Keep `seed-data` files valid YAML or text per cloud‑init specs.
* ✅ Avoid spaces in directory names to keep things consistent.
* ✅ Remove obsolete OS/version folders to prevent them appearing in the prompts.
* ✅ Commit new templates into version control so they’re available to others.

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
