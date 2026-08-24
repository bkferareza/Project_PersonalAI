# Matasuri development-state safety

The Debug x64 app writes its durable files through
`%LOCALAPPDATA%\Machine`. For the packaged Debug app, Windows redirects that
location into:

```text
%LOCALAPPDATA%\Packages\<Matasuri package family>\LocalCache\Local\Machine
```

The former VS Code `postDebugTask` unregistered the package after every debug
session. Package removal also removed that redirected package-local data. The
normal `winapp` launch now builds and updates/registers the same package identity
in place and has no unregister, `Remove-AppxPackage`, or clean step.

The explicitly named destructive unregister task is the sole development
removal path. It verifies the registered package and exact Debug executable,
uses `matasuri-dev://shutdown`, waits for that exact resident PID to exit, and
then snapshots current durable JSON before invoking unregister. A missing,
unreadable, incompatible, incomplete, or checksum-invalid snapshot aborts the
unregister.

Snapshots default to:

```text
%LOCALAPPDATA%\Matasuri\DevelopmentBackups
```

Set `MATASURI_DEVELOPMENT_BACKUP_ROOT` or pass `-BackupRoot` to choose another
user-local location. Each successful snapshot contains only the durable-file
allowlist plus `manifest.json`, with sizes, SHA-256 checksums, readable schema
metadata, package identity, app version, and commit. The latest five verified
snapshots are retained.

Restore is never automatic. Use the guarded restore task with an exact snapshot
directory. It validates the selected manifest/files, gracefully stops the exact
resident, snapshots current state, rejects schema downgrade or mismatch, stages
all files beside their targets, atomically replaces each target, revalidates the
result, and only then relaunches.

These external snapshots protect development deployments. They do not alter the
semantics of an intentional production uninstall and are not immortal product
data.
