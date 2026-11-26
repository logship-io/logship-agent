#!/bin/sh

fail() { printf '%s\n' "$*" >&2; exit 1; }

service_running() {
    case "$platform" in
        linux) systemctl is-active --quiet logship-agent.service ;;
        macos) launchctl print system/io.logship.agent >/dev/null 2>&1 ;;
        openwrt) "$service_file" running >/dev/null 2>&1 ;;
    esac
}

stop_service() {
    case "$platform" in
        linux) systemctl stop logship-agent.service ;;
        macos) launchctl bootout system/io.logship.agent ;;
        openwrt) "$service_file" stop ;;
    esac
}

start_service() {
    case "$platform" in
        linux) systemctl start logship-agent.service ;;
        macos) launchctl bootstrap system "$service_file" ;;
        openwrt) "$service_file" start ;;
    esac
    attempts=0
    until service_running; do
        attempts=$((attempts + 1))
        if [ "$attempts" -ge 5 ]; then
            printf '%s\n' "The service did not enter its running state." >&2
            return 1
        fi
        sleep 1
    done
}

set_autostart() {
    case "$platform:$1" in
        linux:yes) systemctl enable logship-agent.service ;;
        linux:no) systemctl disable logship-agent.service ;;
        macos:yes) launchctl enable system/io.logship.agent ;;
        macos:no) launchctl disable system/io.logship.agent ;;
        openwrt:yes) "$service_file" enable ;;
        openwrt:no) "$service_file" disable ;;
    esac
}

write_service() {
    case "$platform" in
        linux) cat <<'UNIT'
[Unit]
Description=Logship Agent
Wants=network-online.target
After=network-online.target

[Service]
Type=notify
User=logship-agent
Group=logship-agent
WorkingDirectory=/etc/logship-agent
ExecStart=/opt/logship-agent/Logship.Agent.ConsoleHost --contentRoot /etc/logship-agent
Restart=on-failure
TimeoutStartSec=30s

[Install]
WantedBy=multi-user.target
UNIT
            ;;
        macos) cat <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key><string>io.logship.agent</string>
    <key>ProgramArguments</key>
    <array>
        <string>/Library/Application Support/Logship Agent/bin/Logship.Agent.ConsoleHost</string>
        <string>--contentRoot</string>
        <string>/Library/Application Support/Logship Agent</string>
    </array>
    <key>WorkingDirectory</key><string>/Library/Application Support/Logship Agent</string>
    <key>RunAtLoad</key><true/>
    <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>
    <key>StandardOutPath</key><string>/Library/Logs/Logship Agent/agent.log</string>
    <key>StandardErrorPath</key><string>/Library/Logs/Logship Agent/agent-error.log</string>
</dict>
</plist>
PLIST
            ;;
        openwrt) cat <<'INIT'
#!/bin/sh /etc/rc.common
START=95
USE_PROCD=1

start_service() {
    procd_open_instance
    procd_set_param command /usr/lib/logship-agent/Logship.Agent.ConsoleHost --contentRoot /etc/logship-agent
    procd_set_param workdir /etc/logship-agent
    procd_set_param stdout 1
    procd_set_param stderr 1
    procd_set_param respawn
    procd_close_instance
}
INIT
            ;;
    esac
}

cleanup() {
    result=$1
    trap - 0
    set +e
    if [ "$result" -ne 0 ] && [ "$installing" = yes ]; then
        printf '%s\n' "Installation failed. Restoring previous files and service state." >&2
        if [ "$service_changed" = yes ] && service_running; then stop_service || result=2; fi
        if [ "$autostart_changed" = yes ]; then set_autostart "$was_enabled" || result=2; fi
        if [ "$binary_changed" = yes ]; then
            if [ -f "$stage/previous-agent" ]; then
                mv -f "$stage/previous-agent" "$binary" || result=2
            else
                rm -f "$binary" || result=2
            fi
        fi
        if [ "$config_changed" = yes ]; then
            if [ -f "$config_stage/previous-settings" ]; then
                mv -f "$config_stage/previous-settings" "$config_dir/appsettings.json" || result=2
            else
                rm -f "$config_dir/appsettings.json" || result=2
            fi
        fi
        if [ "$new_service" = yes ]; then
            rm -f "$service_file" || result=2
            if [ "$platform" = linux ]; then systemctl daemon-reload || result=2; fi
        fi
        if [ "$was_running" = yes ] && ! service_running; then start_service || result=2; fi
    fi
    if [ "$result" -eq 2 ] && [ "$installing" = yes ]; then
        printf 'Rollback needs attention. Staged files: %s and %s\n' "$stage" "$config_stage" >&2
    else
        [ -z "$config_stage" ] || rm -rf "$config_stage"
        [ -z "$stage" ] || rm -rf "$stage"
    fi
    exit "$result"
}

main() {
    set -eu
    umask 077
    version=
    settings=
    start=no
    while [ "$#" -gt 0 ]; do
        case "$1" in
            --version) [ "$#" -ge 2 ] || fail "Missing release tag."; version=$2; shift ;;
            --config) [ "$#" -ge 2 ] || fail "Missing configuration path."; settings=$2; shift ;;
            --start) start=yes ;;
            -h|--help)
                printf '%s\n' "Usage: install.sh [--version vX.Y.Z] [--config /path/to/appsettings.json] [--start]"
                return ;;
            *) fail "Unknown option: $1" ;;
        esac
        shift
    done
    [ "$(id -u)" -eq 0 ] || fail "Run this installer as root."
    if [ -n "$settings" ]; then
        case "$settings" in /*) ;; *) settings=$PWD/$settings ;; esac
        [ -f "$settings" ] || fail "Configuration file does not exist: $settings"
    fi
    os=$(uname -s)
    case "$(uname -m)" in
        x86_64|amd64) architecture=x64 ;;
        aarch64|arm64) architecture=arm64 ;;
        *) fail "Unsupported architecture. Logship Agent publishes x64 and arm64 artifacts." ;;
    esac
    template=appsettings.json
    case "$os" in
        Darwin)
            platform=macos
            runtime=osx-$architecture
            config_dir="/Library/Application Support/Logship Agent"
            bin_dir=$config_dir/bin
            service_file=/Library/LaunchDaemons/io.logship.agent.plist
            ;;
        Linux)
            config_dir=/etc/logship-agent
            if [ -f /etc/openwrt_release ] && [ -x /sbin/procd ]; then
                platform=openwrt
                runtime=linux-musl-$architecture
                bin_dir=/usr/lib/logship-agent
                service_file=/etc/init.d/logship-agent
                template=appsettings.openwrt.json
            else
                command -v systemctl >/dev/null && [ -d /run/systemd/system ] ||
                    fail "This installer requires a running systemd instance or OpenWrt procd."
                platform=linux
                runtime=linux-$architecture
                for loader in /lib/ld-musl-*.so.1; do
                    [ ! -e "$loader" ] || runtime=linux-musl-$architecture
                done
                bin_dir=/opt/logship-agent
                service_file=/etc/systemd/system/logship-agent.service
            fi
            ;;
        *) fail "Unsupported OS: $os" ;;
    esac
    for dependency in curl unzip mktemp; do
        command -v "$dependency" >/dev/null || fail "Install $dependency before running this installer."
    done
    if command -v sha256sum >/dev/null; then
        checksum() { sha256sum "$1"; }
    elif command -v shasum >/dev/null; then
        checksum() { shasum -a 256 "$1"; }
    else
        fail "Install sha256sum or shasum before running this installer."
    fi

    repo=https://github.com/logship-io/logship-agent
    if [ -z "$version" ]; then
        release_url=$(curl -fsSL -o /dev/null -w '%{url_effective}' "$repo/releases/latest")
        case "$release_url" in
            "$repo/releases/tag/"*) version=${release_url##*/} ;;
            *) fail "Could not resolve the latest Logship Agent release." ;;
        esac
    fi
    case "$version" in
        v[0-9]*) ;;
        *) fail "Invalid release tag: $version" ;;
    esac
    case "$version" in *[!a-zA-Z0-9._-]*) fail "Invalid release tag: $version" ;; esac

    binary=$bin_dir/Logship.Agent.ConsoleHost
    for managed_path in "$bin_dir" "$config_dir" "$binary" "$config_dir/appsettings.json" "$service_file"; do
        [ ! -L "$managed_path" ] || fail "Expected a regular installation path: $managed_path"
    done
    mkdir -p "$bin_dir" "$config_dir"
    stage=
    config_stage=
    installing=no
    binary_changed=no
    config_changed=no
    service_changed=no
    new_service=no
    autostart_changed=no
    was_running=no
    was_enabled=no
    trap 'cleanup "$?"' 0
    trap 'exit 1' HUP INT TERM
    stage=$(mktemp -d "$bin_dir/.install.XXXXXX")
    config_stage=$(mktemp -d "$config_dir/.install.XXXXXX")
    archive=LogshipAgent-$runtime.zip
    curl -fsSL "$repo/releases/download/$version/$archive" -o "$stage/$archive"
    curl -fsSL "$repo/releases/download/$version/$archive.sha256" -o "$stage/$archive.sha256"
    expected=$(awk 'NR == 1 { print $1 }' "$stage/$archive.sha256")
    actual=$(checksum "$stage/$archive" | awk '{ print $1 }')
    [ "${#expected}" -eq 64 ] && [ "$actual" = "$expected" ] || fail "Release archive checksum verification failed."
    unzip -p "$stage/$archive" Logship.Agent.ConsoleHost > "$stage/agent"
    chmod 755 "$stage/agent"
    if [ -f "$service_file" ]; then
        [ -f "$binary" ] || fail "A service already exists at $service_file without the expected installed binary."
        if service_running; then was_running=yes; fi
        case "$platform" in
            linux) if systemctl is-enabled --quiet logship-agent.service; then was_enabled=yes; fi ;;
            macos) if ! launchctl print-disabled system | grep -Eq '"io\.logship\.agent" => (disabled|true)'; then was_enabled=yes; fi ;;
            openwrt) if "$service_file" enabled; then was_enabled=yes; fi ;;
        esac
        if [ "$platform" = macos ] && [ "$was_running:$was_enabled" = yes:no ]; then
            fail "Stop the disabled launch daemon before upgrading it."
        fi
    elif [ -n "$settings" ]; then
        start=yes
    fi

    if [ -n "$settings" ]; then
        cp "$settings" "$config_stage/appsettings.json"
    elif [ -f "$config_dir/appsettings.json" ]; then
        cp "$config_dir/appsettings.json" "$config_stage/appsettings.json"
    else
        unzip -p "$stage/$archive" "$template" > "$config_stage/appsettings.json"
    fi
    if [ "$start" = yes ] || [ "$was_running" = yes ]; then
        "$stage/agent" --contentRoot "$config_stage" --validate-config=true
    else
        "$stage/agent" --contentRoot "$config_stage" --validate-config=true \
            --Output:endpoint=http://localhost:5000 --Output:account=00000000-0000-0000-0000-000000000001
    fi

    if [ "$platform" = linux ]; then
        if ! id logship-agent >/dev/null 2>&1; then
            useradd --system --user-group --no-create-home --shell /bin/false logship-agent
        fi
        chgrp logship-agent "$config_dir"
        chmod 750 "$config_dir"
        chmod 755 "$bin_dir"
        state_dir=$config_dir/logship-agent-local-storage
        if [ ! -d "$state_dir" ]; then
            mkdir "$state_dir"
            chown logship-agent:logship-agent "$state_dir"
        fi
    elif [ "$platform" = macos ]; then
        mkdir -p "/Library/Logs/Logship Agent"
    fi

    installing=yes
    if [ "$was_running" = yes ]; then service_changed=yes; stop_service; fi
    if [ -f "$binary" ]; then cp -p "$binary" "$stage/previous-agent"; fi
    binary_changed=yes
    mv -f "$stage/agent" "$binary"
    if [ -n "$settings" ] || [ ! -f "$config_dir/appsettings.json" ]; then
        if [ -f "$config_dir/appsettings.json" ]; then
            cp -p "$config_dir/appsettings.json" "$config_stage/previous-settings"
        fi
        if [ "$platform" = linux ]; then
            chgrp logship-agent "$config_stage/appsettings.json"
            chmod 640 "$config_stage/appsettings.json"
        fi
        config_changed=yes
        mv -f "$config_stage/appsettings.json" "$config_dir/appsettings.json"
    fi
    if [ ! -f "$service_file" ]; then
        # A plist in LaunchDaemons would otherwise load on the next boot.
        if [ "$platform" = macos ]; then set_autostart no; fi
        new_service=yes
        write_service > "$service_file"
        case "$platform" in
            linux) chmod 644 "$service_file"; systemctl daemon-reload ;;
            macos) chmod 644 "$service_file" ;;
            openwrt) chmod 755 "$service_file" ;;
        esac
    fi
    if [ "$start" = yes ]; then
        autostart_changed=yes
        set_autostart yes
    fi
    if [ "$start" = yes ] || [ "$was_running" = yes ]; then
        service_changed=yes
        start_service
    fi

    installing=no
    printf 'Installed Logship Agent %s (%s).\nConfiguration: %s/appsettings.json\n' "$version" "$runtime" "$config_dir"
    if [ "$start" = no ] && [ "$was_running" = no ]; then
        printf '%s\n' "Set the backend and enrollment settings in the configuration, then rerun the installer with --start."
    fi
}

# Keep the only entry point at EOF so an interrupted download cannot run a partial script.
main "$@"
