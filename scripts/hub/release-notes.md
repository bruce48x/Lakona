Windows: download **Lakona.Hub.Setup.exe**. It installs for the current user and supports offline installation, repair, and automatic updates.

Existing MSI users: close Hub and run the EXE once. After the new Hub starts successfully, choose **Remove old installation** to finish migration. Windows may request administrator permission to remove the old MSI. If you cancel, the new Hub remains usable; run setup again to finish migration. Projects, settings and downloaded SDKs are retained. Older MSI clients cannot perform this migration through their built-in updater. MSI packages are no longer published.

Windows publisher signing is not configured yet, so Windows may display an unknown-publisher warning. Update payloads are independently signed and verified by Hub.
