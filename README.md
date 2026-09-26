# Logship Agent [![.NET](https://github.com/logship-io/logship-agent/actions/workflows/dotnet.yml/badge.svg)](https://github.com/logship-io/logship-agent/actions/workflows/dotnet.yml) [![Build & Release](https://github.com/logship-io/logship-agent/actions/workflows/podman-image.yml/badge.svg)](https://github.com/logship-io/logship-agent/actions/workflows/podman-image.yml)

The Logship agent collects logs and metrics and uploads them to your Logship instance.

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
      "Default": "Information",
      "System.Net.Http.HttpClient.Default": "Warning"
    }
  }
}
```

`Output.endpoint` is the backend base URL; the agent adds its API paths. `Output.account` selects the account that receives the records. `Output.registration.registrationToken` authenticates the agent initially. `Output.dataPath` stores its refresh token and file checkpoints, so keep it on a persistent volume.

This example collects system information visible inside the container and new lines appended to `*.log` files in `/logs`. Set `startAtBeginning` to `true` to ingest existing file contents. To select other collectors, see the [full configuration](src/ConsoleHost/appsettings.json) and [examples](examples). The full configuration enables several collectors and includes machine-specific paths; adjust it before use.

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

After the first successful registration, remove the `registrationToken` value from the configuration and restart the container with `docker restart logship-agent`. The agent then uses the refresh token saved in `logship-agent-data`. Keep that volume when replacing or upgrading the container. A configured registration token takes precedence over the saved token on every startup.

Environment variables can override JSON settings, using two underscores for each nesting level: `Output__endpoint`, `Output__account`, `Output__registration__registrationToken`, and `Output__dataPath`. For example, add `--env Output__endpoint=https://your-backend.example.com` to `docker run`. Keep registration tokens out of source control.

## Check that records are arriving

Check `docker logs logship-agent` for authentication errors and confirm records appear in the configured account in Logship. For detailed upload logs, set `Logging.LogLevel.Default` to `Debug` and restart the container.

- Authentication failures: check the account ID and registration token. An agent without upload permission may wait for approval in your instance; inspect the device verification ID in its logs.
- Connection failures: check the backend URL, port, TLS certificate, and container networking. The frontend URL is not the ingestion endpoint.
- No file records: check the `/logs` mount, file permissions, and `include` pattern. With `startAtBeginning: false`, append a new line to a matching log file to test ingestion.

## Publishing images from CI

The [Build & Release workflow](.github/workflows/podman-image.yml) runs when a `v*` Git tag is pushed. It builds the container once and publishes the release tag and `latest` to both `cr.logship.io/logship-agent` and GHCR.

Configure GitHub Actions repository secrets `LSCR_USER` and `LSCR_PASS` with credentials that can push `cr.logship.io/logship-agent`. These match the credential names used by the backend build. GHCR uses the workflow's `GITHUB_TOKEN` with `packages: write`; the native GitHub release job uses `contents: write`. The Azure pipeline packages the .NET tool; container publishing is handled by GitHub Actions.

## Native releases and local stack

- Download a ZIP from [GitHub Releases](https://github.com/logship-io/logship-agent/releases/latest), place your configured `appsettings.json` next to `Logship.Agent.ConsoleHost` or `Logship.Agent.ConsoleHost.exe`, and run the binary from that directory. Use a writable local directory for `Output.dataPath`.
- For a full local stack, use the deployment installer in [logship-deployments](https://github.com/logship-io/logship-deployments): `curl -fsSL https://raw.githubusercontent.com/logship-io/logship-deployments/main/src/shell/install.sh | sh`.

Additional agent documentation is available on [logship.io](https://logship.io/docs/category/agent).
