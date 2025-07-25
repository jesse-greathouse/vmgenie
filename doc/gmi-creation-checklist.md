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
   record root/user credentials in a secure, device-independent location.

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

* **Hyper-V Settings/Wizard**
  * Name the GMI with the Operating System Name, then the version
  * Ex: `Debian 12`
  * Choose Generation 2 (Generation 1 is not supported)
  * Choose a memory amount, recommended is 4096
  * Check "Use Dynamic memory for this machine" (Highly Recommended)
  * Choose a virtual switch
  * Choose "Create a virtual hard disk" (Other options are not supported)
  * Name the hard drive the exact same name as the VM (Ex: `Debian 12.vhdx`)
  * Size may vary (Recommended is 250GB)
  * Select "Install an operating system from a bootable CD\DVD ROM"
  * Select the Iso file of the operating system you wish to install
  * Press Finish, and let the VM be created, but don't run or connect yet.

* **Hyper-V Post Wizard Create Settings**
  * Go into the newly created VM in the Hyper-V manager and open the "Settings" manager on the right pane.
  * Click on the "Security Tab"
  * For most Linux Distros, you want to check "secure boot", and select "Microsoft UEFI Certificate Authority" in the Template Dropdown
  * Some Linux Distros may be better off turning off "secure boot".
  * Do not enable Trusted Platform Module
  * It is not recommended to check "Enable Shielding"
  * Click the "Checkpoints" Tab under Management
  * Uncheck "Use automatic checkpoints" (Checkpoints are not desirable for a GMI image).
    * Checkpoints can be used if you know what you're doing but it requires merging back into the parent for distribution.
    * If you distribute a GMI with checkpoints, the import process may fail.
  * Press "Apply" at the bottom to save the settings.

* Recommend minimal RAM/CPU for template image.
* **During OS installation:**
  * **Set strong, unique passwords** for the root account and any privileged/admin users.
  * **Immediately record these credentials** in a secure, device-independent location (e.g., a private Google Doc, password manager, or encrypted note).
    *Do this as soon as the passwords are created—do not rely on memory or loose notes!*
  * Example (record in your secret doc, not in the VM or code repo):

```bash
Debian 12 GMI (Created: 2025-07-25)
  root:  [secure password for root]
  admin: [secure password for admin]
```

* **Reminder:** These are build-time only. These passwords will not be used on derived cloud-init VMs.

* Install OS as normal.

### Step 2: Update & Minimize

* Install or enable sudo

```bash
sudo apt update && sudo apt upgrade -y
sudo apt autoremove -y
sudo apt clean
```

* Remove unused packages (X11, firmware, etc. if headless)
* (Optional) Remove generic users, unnecessary services

### Step 3: Install & Prepare cloud-init

**Check if cloud-init is already installed:**

```bash
dpkg -l | grep cloud-init
```

* If there’s *no output*, it’s **not installed**.

**Install cloud-init:**

```bash
sudo apt install -y cloud-init
```

**Verify installation:**

```bash
which cloud-init
```

* Should return a path like `/usr/bin/cloud-init`.

**Check service status:**

```bash
systemctl status cloud-init
```

* On a fresh install, it may show “inactive” or “exited”—that’s normal unless provisioning is happening.
* Ensure it’s set to use DataSource `NoCloud` by default (edit `/etc/cloud/cloud.cfg.d/99-gmi.cfg` as needed)
* Reset any previous cloud-init run:

```bash
sudo cloud-init clean --logs
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

```bash
# Build the hyperV daemons from source 
# only if hv-kvp-daemon.service and hv-vss-daemon.service do not exist on the system

# install the build tools
sudo apt update
sudo apt install -y build-essential git bc flex bison libssl-dev libelf-dev

# Download the linux kernel source code
cd /usr/src
KVER=$(uname -r)
sudo wget https://cdn.kernel.org/pub/linux/kernel/v${KVER%%.*}.x/linux-$KVER.tar.xz
sudo tar -xf linux-$KVER.tar.xz
cd linux-$KVER

# Build the hyperV daemons
cd tools/hv
make

# move the created binaries to their correct location
sudo cp hv_kvp_daemon hv_vss_daemon hv_fcopy_daemon /usr/local/sbin/

# Create or update systemd service units (if missing)
sudo tee /etc/systemd/system/hv-kvp-daemon.service > /dev/null <<'EOF'
[Unit]
Description=Hyper-V key-value pair (KVP) daemon
After=network.target

[Service]
ExecStart=/usr/local/sbin/hv_kvp_daemon -n
Restart=always

[Install]
WantedBy=multi-user.target
EOF

sudo tee /etc/systemd/system/hv-vss-daemon.service > /dev/null <<'EOF'
[Unit]
Description=Hyper-V volume shadow copy service (VSS) daemon
After=network.target

[Service]
ExecStart=/usr/local/sbin/hv_vss_daemon -n
Restart=always

[Install]
WantedBy=multi-user.target
EOF

sudo tee /etc/systemd/system/hv-fcopy-daemon.service > /dev/null <<'EOF'
[Unit]
Description=Hyper-V file copy service daemon
After=network.target

[Service]
ExecStart=/usr/local/sbin/hv_fcopy_daemon -n
Restart=always

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl start hv-kvp-daemon.service
sudo systemctl start hv-vss-daemon.service

# Optionally, disable and stop hv-fcopy-daemon if not needed:
sudo systemctl disable --now hv-fcopy-daemon.service

# delete the source code used for building
sudo rm -rf /usr/src/linux-*
```

> If you are forced to build these daemons by hand, record exactly which source was used, any deviations from upstream, and the steps required, so that this process is repeatable in future GMI builds or for other engineers.

* You should explicitly enable both daemons to guarantee they always run on GMI clones

```bash
sudo systemctl enable hv-kvp-daemon.service
sudo systemctl enable hv-vss-daemon.service
```

* Both should now report: enabled

### Step 5: Clean-Up & Zero Fill

* Vacuum logs:

```bash
sudo journalctl --vacuum-time=1d
```

* Hide or Skip Grub loader at startup

```bash
sudo nano /etc/default/grub

# edit the file
GRUB_DEFAULT=0
GRUB_TIMEOUT=0
GRUB_HIDDEN_TIMEOUT=0
GRUB_HIDDEN_TIMEOUT_QUIET=true
# save the file

sudo update-grub
```

* Cleanup artifacts

```bash
sudo rm -f ~/.bash_history /root/.bash_history
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

* On the Host:

* *If the VM has a differencing disk (`.avhdx`) after shutdown:*
  * **Open Hyper-V Manager** on your host system.
    * In the **Actions** pane, select **Edit Disk…**
  * **Browse to and select the `.avhdx` file** (not the `.vhdx`).
    * Follow the Edit Disk wizard:
  * Choose **Merge** when prompted for an action.
  * Select **To the parent virtual hard disk** (recommended).
  * Click **Finish** and wait for the merge to complete.
  * After merging, **only the `.vhdx` file should remain** (no `.avhdx`).
  * Double-check the VM’s settings to ensure the hard drive points to the consolidated `.vhdx` file.

* Compact the virtual hard disk

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
