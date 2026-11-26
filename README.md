# Logship Agent [![.NET](https://github.com/logship-io/logship-agent/actions/workflows/dotnet.yml/badge.svg)](https://github.com/logship-io/logship-agent/actions/workflows/dotnet.yml) [![Build & Release](https://github.com/logship-io/logship-agent/actions/workflows/podman-image.yml/badge.svg)](https://github.com/logship-io/logship-agent/actions/workflows/podman-image.yml)

The Logship agent collects logs and metrics and uploads them to your Logship instance.

## Install the native agent

The installers download a self-contained release, check its SHA-256 checksum, and register the agent with the host's service manager. Native releases target x64 and arm64. You do not need to install .NET separately.

**Linux (systemd) and macOS**. Install `curl`, `unzip`, and `sha256sum` or `shasum`. On Linux, the installer uses `useradd` to create the service account. Run:

```sh
curl -fsSL https://raw.githubusercontent.com/logship-io/logship-agent/master/install.sh | sudo bash
```

**Windows**. In an elevated PowerShell window, download and run the installer:

```powershell
Invoke-WebRequest -UseBasicParsing https://raw.githubusercontent.com/logship-io/logship-agent/master/install.ps1 -OutFile install.ps1
.\install.ps1
```

**OpenWrt**. The installer selects the Linux musl release on x64 and arm64. OpenWrt support is experimental pending runtime verification on both architectures. Use a device with procd, sufficient persistent storage, HTTPS CA certificates, the native libraries needed by the binary, and `curl`, `unzip`, and `sha256sum`. Run from a root shell:

```sh
curl -fsSL https://raw.githubusercontent.com/logship-io/logship-agent/master/install.sh | sh
```

On a fresh install, edit the installed `appsettings.json` with your backend URL, account ID, and registration token. Rerun the installer with `--start` (or `-Start` in PowerShell) to validate the settings and enable and start the service. For example, use `sudo bash -s -- --start` on the receiving side of the Linux or macOS pipeline. To supply an existing configuration file, pass `--config /path/to/appsettings.json` or `-Config C:\path\to\appsettings.json`. Supplying a configuration starts a fresh installation and explicitly replaces the installed configuration on upgrades.

| Platform | Configuration file | Service status and logs |
| --- | --- | --- |
| Linux | `/etc/logship-agent/appsettings.json` | `systemctl status logship-agent` and `journalctl -u logship-agent` |
| macOS | `/Library/Application Support/Logship Agent/appsettings.json` | `sudo launchctl print system/io.logship.agent` and `/Library/Logs/Logship Agent/` |
| Windows | `%ProgramData%\Logship Agent\appsettings.json` | `Get-Service logship.agent` and the Application event log, source `Logship.Agent.ConsoleHost` |
| OpenWrt | `/etc/logship-agent/appsettings.json` | `/etc/init.d/logship-agent running` and `logread` |

Run the installer again to upgrade. Use `--version vX.Y.Z` or `-Version vX.Y.Z` to select a release. Upgrades preserve the installed settings, saved credentials, checkpoints, and service activation state by default. Linux runs as the `logship-agent` system user and Windows as `LocalService`. Give that identity read access to the sources you explicitly enable and write access to your `Output.dataPath` if you change it.

The releases include a platform preset and a commented [full settings reference](src/ConsoleHost/config/appsettings.full.json). The default sources report basic host inventory hourly, plus process health and uptime once per minute. Detailed agent metrics and traces are opt-in under `Sources.Internals`. They can generate substantial data because buffering and uploading diagnostic records also produces measurements.

Routine logs default to `Warning`. The agent logs once when the host starts and again when Logship accepts its first nonempty upload.

## Pull the container image

```sh
docker pull cr.logship.io/logship-agent:latest
```

Release images are published as `cr.logship.io/logship-agent:<release-tag>` (including the `v` prefix) and `:latest`. Use a release tag to pin a deployment. The same images are also published to `ghcr.io/logship-io/logship-agent`. The container build currently targets Linux x64; native releases support additional platforms. You can substitute `podman` for `docker` in the commands below.

## Configure the agent for your Logship instance

You need the **backend API URL**, your **account ID (a UUID)**, and an **agent registration token** for that account from your Logship instance. Point the agent at the database/backend service. For example, the local backend stack uses `http://localhost:5000` when accessed through the host network.

Create a file named `appsettings.json` with the following contents. Replace the example endpoint, account ID, and token before starting the container:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Default": { "Url": "http://*:57421" }
    }
  },
  "Output": {
    "endpoint": "https://YOUR-LOGSHIP-BACKEND",
    "account": "YOUR-ACCOUNT-UUID",
    "interval": "00:00:02",
    "dataPath": "/var/lib/logship-agent",
    "registration": {
      "registrationToken": "YOUR-AGENT-REGISTRATION-TOKEN"
    }
  },
  "Sources": {
    "SystemInformation": { "enabled": true, "interval": "00:00:30" },
    "LogFile": {
      "enabled": true,
      "workingDirectory": "/logs",
      "include": ["*.log"],
      "startAtBeginning": false
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "System.Net.Http.HttpClient.Default": "Warning"
    }
  }
}
```

`Output.endpoint` is the backend base URL; the agent adds its API paths. `Output.account` selects the account that receives the records. `Output.registration.registrationToken` authenticates the agent initially. `Output.dataPath` stores its refresh token and file checkpoints, so keep it on a persistent volume.

This example collects system information visible inside the container and new lines appended to `*.log` files in `/logs`. Set `startAtBeginning` to `true` to ingest existing file contents. To select other collectors, see the [full settings reference](src/ConsoleHost/config/appsettings.full.json) and [examples](examples).

## Run the container

The following commands use a Linux shell. Run them from the directory containing your `appsettings.json`. Replace `/absolute/path/to/your/logs` with the host directory containing the log files you want to collect:

```sh
docker volume create logship-agent-data
docker run -d \
  --name logship-agent \
  --restart unless-stopped \
  --mount type=bind,source="$(pwd)/appsettings.json",target=/app/appsettings.json,readonly \
  --mount type=bind,source=/absolute/path/to/your/logs,target=/logs,readonly \
  --mount type=volume,source=logship-agent-data,target=/var/lib/logship-agent \
  cr.logship.io/logship-agent:latest
docker logs -f logship-agent
```

The configuration file and log directory must exist before running this command. If you only want system information, set `Sources.LogFile.enabled` to `false` and omit the `/logs` mount.

For a backend running on the **same Linux host**, add `--network host` and set `Output.endpoint` to its host address, such as `http://localhost:5000`. Without host networking, `localhost` refers to the agent container. For a backend in another container, attach the agent to the same Docker network with `--network <network-name>` and use the backend service name and internal port, such as `http://logship-database:5000`. For a remote instance, use its reachable backend URL.

After the first successful registration, remove the `registrationToken` value from the configuration and restart the container with `docker restart logship-agent`. The agent then uses the refresh token saved in `logship-agent-data`. Keep that volume when replacing or upgrading the container. The saved refresh token takes precedence on startup. A configured registration token belongs to one pre-approved agent. Use a separate token for each installation. Pending agents also keep a private enrollment proof in the data directory so a retried request reuses the same pending registration.

Environment variables can override JSON settings, using two underscores for each nesting level: `Output__endpoint`, `Output__account`, `Output__registration__registrationToken`, and `Output__dataPath`. For example, add `--env Output__endpoint=https://your-backend.example.com` to `docker run`. Keep registration tokens out of source control.

## Check that records are arriving

Check `docker logs logship-agent` for authentication errors and confirm records appear in the configured account in Logship. For detailed upload logs, set `Logging.LogLevel.Default` to `Debug` and restart the container.

- Authentication failures: check the account ID and registration token. An agent without upload permission may wait for approval in your instance; inspect the device verification ID in its logs.
- Connection failures: check the backend URL, port, TLS certificate, and container networking. The frontend URL is not the ingestion endpoint.
- No file records: check the `/logs` mount, file permissions, and `include` pattern. With `startAtBeginning: false`, append a new line to a matching log file to test ingestion.

## USB devices and connection events

Enable the USB source on Windows or Linux by adding this entry under `Sources`:

```json
"USB": { "enabled": true, "interval": "00:00:30" }
```

USB collection is disabled by default. It covers storage, keyboards, cameras, hubs,
and internal USB peripherals. Host controllers, root hubs, and individual interfaces
of composite devices are excluded.

`System.USB.Devices` publishes one record per connected device at startup and each
inventory interval. Empty inventories produce no records. `System.USB.Events`
publishes `Action` (`Connected` or `Disconnected`) and `DetectionMethod` (`Native`
or `Reconciliation`). Existing devices at startup establish the baseline without
connection events. Restarting does not replay changes from the time the agent was stopped.

Both schemas contain `machine` and `DeviceId`, plus `VendorId`, `ProductId`,
`Manufacturer`, `Product`, `SerialNumber`, and `Location` when available. Vendor
and product IDs are four-digit lowercase hexadecimal strings. Device identity is
platform-specific and identifies an attachment; it is not guaranteed across ports
or machines. Timestamps represent agent observation time. There are no snapshot IDs
or completion records; use events to track removals rather than absence of inventory rows.

Windows uses Configuration Manager device notifications and needs no interactive
desktop. Linux reads `/sys/bus/usb/devices` and uses `libudev.so.1` for notifications.
If native monitoring is unavailable, the agent logs a warning, continues periodic
inventory/change detection, and retries monitoring each interval. This fallback
can miss brief connections. Failed scans retain previous state.

Linux containers need read access to the host USB sysfs tree for inventory. Native
notifications additionally require visibility of host udev events (typically the
host network namespace for the netlink socket, with host `/run/udev` visible where
required by the environment) and libudev installed in the container. A sysfs mount
alone does not provide native events. Validate event delivery in the deployment;
container isolation can prevent delivery even when subscription succeeds.

## Storage, network, and power events

The checked-in configuration enables three additional event sources on Windows and Linux:

```json
"StorageEvents": { "enabled": true, "interval": "00:00:05" },
"NetworkEvents": { "enabled": true, "interval": "00:00:15" },
"PowerEvents": { "enabled": true }
```

Each source can be disabled independently. Code defaults remain disabled when these
sections are omitted. Existing `System.Storage` and `System.Network.*` inventory
collectors keep their existing schemas and intervals.

| Schema | Actions | Event-specific fields, when available |
| --- | --- | --- |
| `System.Storage.Events` | `DiskAdded`, `DiskRemoved`, `VolumeAdded`, `VolumeRemoved`, `Mounted`, `Unmounted` | `Kind`, `DeviceId`, `VolumeId`, `VolumeUuid`, `VolumeLabel`, `FileSystem`, `MountLocation`; Linux mounts also include `MountId` and `MountRoot` |
| `System.Network.Events` | `InterfaceAdded`, `InterfaceRemoved`, `LinkUp`, `LinkDown`, `AddressAdded`, `AddressRemoved`, `RouteAdded`, `RouteRemoved` | `InterfaceIndex`, `InterfaceName`, `InterfaceId`, `Address`, `PrefixLength`, `Destination`, `Gateway`, `Metric`, `Table`, `RouteType` |
| `System.Power.Events` | `Suspending`, `Resumed`, `ShutdownRequested` | Windows shutdowns may include `EventRecordId`, `SourceTimestamp`, `Process`, `Reason`, `ReasonCode`, `ShutdownType`, `Comment`, `User` |

All records contain `machine`, `Action`, and `DetectionMethod` (`Native` or
`Reconciliation`). Timestamps are agent observation times. Native means a direct
notification or a scan triggered by one. There are no snapshot IDs. Startup
establishes a baseline without synthetic arrival events, and cached metadata is
used for removal events. A route metric change emits removal followed by addition.
Windows route metrics are the route metric component, excluding the interface metric;
`Table` is zero on Windows and identifies the routing table on Linux.

Storage uses Windows disk/volume device-interface notifications or Linux udev block
events. Mount locations are rescanned after notifications and every five seconds
by default; brief mounts between scans can be missed. Linux includes bind and
network mounts visible in the agent's mount namespace. Windows mount locations
cover local volumes, including mounted folders; mapped network drives are excluded.
USB storage may produce both USB events and storage events describing the same attachment.

Network events use Windows IP Helper notifications or Linux route netlink and
include virtual interfaces, IPv4/IPv6 unicast addresses, and routes. Interface
indices identify the current interface lifetime and can be reused after removal.
No packet capture or per-connection event collection is added.

Failed storage/network scans retain prior state. Failed subscriptions retry every
30 seconds while periodic reconciliation continues. Linux requires `libudev.so.1`
for block notifications and access to sysfs/udev; the container image includes
libudev. Host monitoring from containers additionally requires host mount/network
visibility and native event access as described for USB above. Otherwise records
describe the container's namespaces, and isolation may prevent native delivery.

Power events use Windows suspend/resume callbacks and future User32 event 1074
records from the System event log. Linux uses logind signals via `libsystemd.so.0`
and the system D-Bus socket; logind must be running and accessible. Unavailable
power sources log warnings and retry every 30 seconds independently. No polling
fallback can reconstruct missed power transitions. Duplicate resume callbacks
are suppressed; stopping only the agent does not generate a machine shutdown event.

Suspension/shutdown notices request a serialized flush with a one-second deadline.
After collectors stop, the output service attempts a final flush within the host's
shutdown timeout, capped at five seconds. Delivery is best effort: no historical
replay or durable queue is added, and abrupt shutdown or lost networking can prevent
upload. `ShutdownRequested` reports intent, not proof of completed shutdown. Raw
journal or ETW logs can contain related messages alongside these normalized events.

## Publishing images from CI

Create the release labels below in the repository. Apply exactly one to a PR before merging it into `master`.

| PR label | Next tag if the highest stable tag is `v0.10.1` |
| --- | --- |
| `release:patch` | `v0.10.2` |
| `release:minor` | `v0.11.0` |
| `release:major` | `v1.0.0` |

The [Tag after merge workflow](.github/workflows/tag-after-merge.yml) tags the PR's merge commit and starts [Build & Release](.github/workflows/podman-image.yml) for that tag. It applies the bump to the highest stable `vX.Y.Z` tag, or to `v0.0.0` when there are no stable tags. A PR with multiple release labels fails the workflow before a tag is created.

Manual `v*` tag pushes also run Build & Release. If tagging succeeded and dispatch failed, rerun Tag after merge to reuse the same tag. If the release build failed, rerun Build & Release for that tag. The release workflow publishes native artifacts to GitHub Releases and the container under the release tag and `latest` to both `cr.logship.io/logship-agent` and GHCR.

Configure GitHub Actions repository secrets `LSCR_USER` and `LSCR_PASS` with credentials that can push `cr.logship.io/logship-agent`. These match the credential names used by the backend build. GHCR uses the workflow's `GITHUB_TOKEN` with `packages: write`; the native GitHub release job uses `contents: write`. The Azure pipeline packages the .NET tool; container publishing is handled by GitHub Actions.

## Native releases and local stack

- Download a ZIP from [GitHub Releases](https://github.com/logship-io/logship-agent/releases/latest), place your configured `appsettings.json` next to `Logship.Agent.ConsoleHost` or `Logship.Agent.ConsoleHost.exe`, and run the binary from that directory. Use a writable local directory for `Output.dataPath`.
- For a full local stack, use the deployment installer in [logship-deployments](https://github.com/logship-io/logship-deployments): `curl -fsSL https://raw.githubusercontent.com/logship-io/logship-deployments/main/src/shell/install.sh | sh`.

Additional agent documentation is available on [logship.io](https://logship.io/docs/category/agent).
