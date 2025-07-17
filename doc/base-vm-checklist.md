# Base VM Finalization Checklist

Use this checklist to prepare and finalize the base VM for use with `vmgenie`.

This ensures the VM is lean, properly configured, and ready to be cloned and provisioned.

---

## ✅ Preparation

- [ ] Boot the VM and log in as a privileged user.
- [ ] Ensure all OS updates are applied:

```bash
  sudo apt update && sudo apt upgrade -y
  sudo apt autoremove -y
````

- [ ] Clean apt cache:

  ```bash
  sudo apt clean
  ```

---

## 🔑 Hyper-V Integration

- [ ] Verify the Hyper-V kernel modules are loaded:

```bash
lsmod | grep hv
```

  You should see at least:

- `hv_vmbus`
- `hv_netvsc`
- `hv_storvsc`
- `hv_utils`
- `hv_balloon`

- [ ] Install & enable Hyper-V daemons (`hv_kvp_daemon`, `hv_vss_daemon`):

  - Build them if necessary from `linux/tools/hv/` and install into `/usr/local/sbin/`
  - Create and enable `systemd` units:

    ```bash
    sudo systemctl enable --now hv-kvp-daemon.service
    sudo systemctl enable --now hv-vss-daemon.service
    ```

- [ ] Verify services are running:

  ```bash
  systemctl status hv-kvp-daemon.service
  systemctl status hv-vss-daemon.service
  ```

- [ ] Optional: explicitly disable unused `hv-fcopy-daemon` if present:

  ```bash
  sudo systemctl disable --now hv-fcopy-daemon.service
  ```

---

## 🧹 Cleanup

- [ ] Remove any build artifacts (e.g., Linux kernel repo, tmp files).

  ```bash
  rm -rf ~/linux
  ```

- [ ] Remove `/zero.fill` if present:

  ```bash
  sudo rm -f /zero.fill
  ```

- [ ] Vacuum old system logs:

  ```bash
  sudo journalctl --vacuum-time=1d
  ```

---

## 🪄 Zero Free Space

- [ ] Write zeros into free space to make the `.vhdx` shrinkable:

  ```bash
  sudo dd if=/dev/zero of=/zero.fill bs=1M
  # wait until it errors out
  sudo rm -f /zero.fill
  ```

---

## 🔄 Shutdown & Compact

- [ ] Shut down the VM:

  ```bash
  sudo shutdown -h now
  ```

- [ ] On the Hyper-V host, run:

  ```powershell
  Optimize-VHD -Path 'C:\path\to\base-vm.vhdx' -Mode Full
  ```

---

## 📄 Final Notes

✅ The resulting `.vhdx` should now be minimal, clean, and ready for cloning.
✅ Document the `USERNAME`, `PRIVKEY`, `TIMEZONE`, `LOCALE`, and any other defaults expected by `cloud-init`.

---

### Example Verification

On host:

```powershell
Get-VMNetworkAdapter -VMName 'BaseVM'
Get-VMIntegrationService -VMName 'BaseVM'
```

Ensure:

- IP addresses are reported correctly.
- Integration services are `Enabled` and `OK`.

---

> 📝 Keep this document updated as the process evolves!
