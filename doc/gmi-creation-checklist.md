# Genie Machine Image (GMI) Creation Procedure

**A GMI (“Genie Machine Image”) is a rigorously prepared, cloud-init–capable, Hyper-V virtual machine image, conforming to the vmgenie standard.**
This document details the step-by-step process required to create a GMI from a fresh OS installation, ensuring maximum compatibility, portability, and usability within the vmgenie ecosystem.

---

## 1. What is a GMI?

A **GMI** is more than just a generalized VM export.
It is:

* A Hyper-V virtual machine that has been prepared, optimized, and validated for cloud-init provisioning.
* Cleaned of user-specific or transient data.
* Equipped with the right drivers and daemons for seamless operation on Hyper-V.
* Optionally, includes a small manifest describing its configuration.

GMIs are the Hyper-V equivalent of Amazon’s AMIs: portable, shareable, and reusable.

---

## 2. GMI Creation Workflow Overview

1. **Start from Official OS ISO**
   Download the ISO for your desired OS/version.
   Boot a new VM from it, using only default install options where possible.

2. **Initial Configuration & Update**

   * Log in as root/privileged user.
   * Fully update the OS.
   * Remove any unnecessary packages or files.

3. **Cloud-Init Preparation**

   * Install cloud-init (if not present).
   * Ensure `cloud-init` supports DataSource `NoCloud` (most major distros do).
   * Clean/reset any prior cloud-init state.

4. **Hyper-V Integration**

   * Install and enable all relevant Hyper-V drivers and daemons.
   * Ensure network, KVP, VSS, ballooning, and storage integration is working.
   * (Document any OS-specific quirks.)

5. **Image Minimization & Clean-Up**

   * Remove any build tools, caches, logs, etc.
   * Vacuum logs and free space.
   * Zero out unused space for optimal compacting.

6. **Shutdown and Export**

   * Shutdown cleanly.
   * Compact VHDX on host.
   * Export VM (with/without configuration files as appropriate).

7. **Document GMI Metadata**

   * Note OS, kernel version, installed integration services, cloud-init version.
   * Store any required defaults (USERNAME, LOCALE, etc.) in a sidecar file.

---

## 3. Step-by-Step GMI Creation

### Step 1: Fresh VM Install

* Use Hyper-V Manager or PowerShell to create a new VM with the official OS ISO.
* Recommend minimal RAM/CPU for template image.
* Install OS, create a generic privileged user (`genie`/`admin`), set a temporary password.

### Step 2: Update & Minimize

```bash
sudo apt update && sudo apt upgrade -y
sudo apt autoremove -y
sudo apt clean
```

* Remove unused packages (X11, firmware, etc. if headless)
* (Optional) Remove generic users, unnecessary services

### Step 3: Install & Prepare cloud-init

* Install `cloud-init` if not present:

  ```bash
  sudo apt install -y cloud-init
  ```

* Ensure it’s set to use DataSource `NoCloud` by default (edit `/etc/cloud/cloud.cfg.d/99-gmi.cfg` as needed)
* Reset any previous cloud-init run:

  ```bash
  sudo cloud-init clean --logs
  ```

* Remove existing SSH keys:

  ```bash
  sudo rm -f /etc/ssh/ssh_host_*
  ```

### Step 4: Hyper-V Integration

* Ensure Hyper-V kernel modules are loaded:

  ```bash
  lsmod | grep hv
  ```

* Ensure `hv_*` daemons are running:

  ```bash
  systemctl status hv-kvp-daemon.service
  systemctl status hv-vss-daemon.service
  ```

* If missing, build from kernel source and install to `/usr/local/sbin/`.
  Document the version built, location, and any patches applied.

### Step 5: Clean-Up & Zero Fill

* Remove all build directories/artifacts:

  ```bash
  sudo rm -rf ~/linux
  ```

* Vacuum logs:

  ```bash
  sudo journalctl --vacuum-time=1d
  ```

* Zero free space:

  ```bash
  sudo dd if=/dev/zero of=/zero.fill bs=1M
  sudo rm -f /zero.fill
  ```

### Step 6: Shutdown & Compact

* Shut down VM:

  ```bash
  sudo shutdown -h now
  ```

* On host:

  ```powershell
  Optimize-VHD -Path 'C:\path\to\base-vm.vhdx' -Mode Full
  ```

### Step 7: Export and Document

* Export the VM from Hyper-V Manager or PowerShell.
* Prepare a directory:

  ```powershell
  /gmirepo/ubuntu-24.04/
    ├── ubuntu-24.04-gmi.vhdx
    ├── gmi-metadata.yml
    └── README.md
  ```

* In `gmi-metadata.yml`, record:

  * OS, kernel, cloud-init, integration services versions
  * Creation date, maintainer, default users, etc.

---

## 4. OS-Specific Notes

* **Ubuntu:** Use latest LTS. Packages for Hyper-V daemons usually available.
* **OpenSUSE:** Some daemons must be built from source—document exact steps.
* **CentOS/RHEL/Alma:** Integration may be incomplete; note what works/doesn’t.
* **Others:** Record any non-standard configs needed for cloud-init, drivers, etc.

---

## 5. Checklist for Validation

* [ ] VM boots in Hyper-V with no errors.
* [ ] Network integration works: DHCP assigns IP.
* [ ] `cloud-init` runs successfully on first boot, sets hostname/user as expected.
* [ ] Integration services status is “OK”.
* [ ] No leftover build artifacts, logs, SSH keys, etc.
* [ ] README documents any caveats or special instructions.

---

## 6. Sharing GMIs

* Package directory as a `.zip` or `.tar.gz`.
* Include checksum and metadata.
* Optionally, submit to a central repository (planned for future).

---

## 7. Future Enhancements

* Automate as much as possible with scripts/Ansible/Packer.
* Provide pre-built GMIs for major distros (where licensing allows).
* Build “GMI validator” utility in vmgenie to scan and verify images.

---

### This is a living document—revise and extend as workflows improve
