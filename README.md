# Logship Agent [![.NET](https://github.com/logsink/logship-agent/actions/workflows/dotnet.yml/badge.svg)](https://github.com/logsink/logship-agent/actions/workflows/dotnet.yml) [![Build & Release](https://github.com/logship-io/logship-agent/actions/workflows/podman-image.yml/badge.svg)](https://github.com/logship-io/logship-agent/actions/workflows/podman-image.yml)

The repository for the logship collector agent.

## Quick start

- Container: `docker run -d --name logship-agent --network host -v $PWD/appsettings.json:/app/appsettings.json:ro ghcr.io/logship-io/logship-agent:latest`
- Native release: download the latest ZIP from [GitHub Releases](https://github.com/logship-io/logship-agent/releases/latest), place `appsettings.json` next to `Logship.Agent.ConsoleHost` or `Logship.Agent.ConsoleHost.exe`, then run that binary.
- Full local stack: use the deployment installer in [logship-deployments](https://github.com/logship-io/logship-deployments) with `curl -fsSL https://raw.githubusercontent.com/logship-io/logship-deployments/main/src/shell/install.sh | sh`

For up-to-date documentation, visit [our website](https://logship.io/docs/category/agent).
