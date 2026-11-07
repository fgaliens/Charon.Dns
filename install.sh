#!/usr/bin/env bash
#
# Charon.Dns installer / updater.
#
# Downloads the latest Charon.Dns release from GitHub for the current architecture,
# installs it as a systemd service and generates configuration templates on first install.
# Existing configuration files are never modified.
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/fgaliens/DNS/master/install.sh | sudo bash -s -- --dir /etc/charon-dns
#   sudo /etc/charon-dns/install.sh            # update an existing installation

set -euo pipefail

readonly REPO="fgaliens/DNS"
readonly APP_BINARY="Charon.Dns"
readonly DEFAULT_DIR="/etc/charon-dns"
readonly DEFAULT_SERVICE="charon-dns"
readonly SERVICE_MARKER=".service-name"
readonly SYSTEMD_DIRS=(/etc/systemd/system /lib/systemd/system /usr/lib/systemd/system)

INSTALL_DIR=""
SERVICE_NAME=""
FORCE=0
TMP_DIR=""

log() { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mWarning:\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

usage() {
    cat <<EOF
Charon.Dns installer / updater

Usage: install.sh [options]

Options:
  -d, --dir <path>       Installation directory (asked interactively if not set, default: $DEFAULT_DIR)
  -s, --service <name>   systemd service name (default: detected, or $DEFAULT_SERVICE)
  -f, --force            Reinstall even if the latest version is already installed
  -h, --help             Show this help

When updating, the installation directory and service name are detected automatically
(from the script location, the '$SERVICE_MARKER' file or existing systemd units).
EOF
}

cleanup() {
    if [[ -n "$TMP_DIR" && -d "$TMP_DIR" ]]; then
        rm -rf "$TMP_DIR"
    fi
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            -d|--dir)
                [[ $# -ge 2 ]] || die "Option $1 requires a value"
                INSTALL_DIR="$2"; shift 2 ;;
            --dir=*)
                INSTALL_DIR="${1#*=}"; shift ;;
            -s|--service)
                [[ $# -ge 2 ]] || die "Option $1 requires a value"
                SERVICE_NAME="$2"; shift 2 ;;
            --service=*)
                SERVICE_NAME="${1#*=}"; shift ;;
            -f|--force)
                FORCE=1; shift ;;
            -h|--help)
                usage; exit 0 ;;
            *)
                die "Unknown option: $1 (see --help)" ;;
        esac
    done
    SERVICE_NAME="${SERVICE_NAME%.service}"
}

check_requirements() {
    [[ "$(uname -s)" == "Linux" ]] || die "Charon.Dns supports Linux only"
    [[ $EUID -eq 0 ]] || die "Root privileges are required (run with sudo)"

    local cmd
    for cmd in curl tar systemctl; do
        command -v "$cmd" >/dev/null 2>&1 || die "Required command not found: $cmd"
    done
}

detect_rid() {
    case "$(uname -m)" in
        x86_64|amd64) echo "linux-x64" ;;
        aarch64|arm64) echo "linux-arm64" ;;
        *) die "Unsupported architecture: $(uname -m) (supported: x86_64, aarch64)" ;;
    esac
}

normalize_dir() {
    local dir="$1"
    [[ "$dir" == /* ]] || dir="$PWD/$dir"
    while [[ "$dir" == */ && "$dir" != "/" ]]; do
        dir="${dir%/}"
    done
    echo "$dir"
}

# Prints "<service name> <working directory>" for each systemd unit running Charon.Dns.
find_existing_services() {
    local unit_dir unit_file exec_start work_dir name
    for unit_dir in "${SYSTEMD_DIRS[@]}"; do
        [[ -d "$unit_dir" ]] || continue
        for unit_file in "$unit_dir"/*.service; do
            [[ -f "$unit_file" ]] || continue
            exec_start="$(grep -m1 -E '^[[:space:]]*ExecStart=' "$unit_file" || true)"
            [[ "$exec_start" == *"/$APP_BINARY"* ]] || continue

            work_dir="$(grep -m1 -E '^[[:space:]]*WorkingDirectory=' "$unit_file" | sed -E 's/^[[:space:]]*WorkingDirectory=-?//' || true)"
            if [[ -z "$work_dir" ]]; then
                work_dir="$(echo "$exec_start" | grep -oE "/[^[:space:]\"']*/$APP_BINARY" | head -n1 || true)"
                work_dir="${work_dir%/"$APP_BINARY"}"
            fi
            [[ -n "$work_dir" ]] || continue

            name="$(basename "$unit_file" .service)"
            echo "$name $(normalize_dir "$work_dir")"
        done
    done | sort -u
}

ask() {
    local prompt="$1" default="$2" answer=""
    if [[ -r /dev/tty ]] && { : </dev/tty; } 2>/dev/null; then
        read -r -p "$prompt [$default]: " answer </dev/tty || true
    fi
    echo "${answer:-$default}"
}

resolve_install_dir() {
    if [[ -n "$INSTALL_DIR" ]]; then
        INSTALL_DIR="$(normalize_dir "$INSTALL_DIR")"
        return
    fi

    # Script is run from an existing installation directory
    local script_path="${BASH_SOURCE[0]:-}"
    if [[ -n "$script_path" && -f "$script_path" ]]; then
        local script_dir
        script_dir="$(cd "$(dirname "$script_path")" && pwd)"
        if [[ -f "$script_dir/$APP_BINARY" ]]; then
            INSTALL_DIR="$script_dir"
            return
        fi
    fi

    # Existing systemd service(s)
    local services
    services="$(find_existing_services)"
    if [[ -n "$SERVICE_NAME" ]]; then
        services="$(echo "$services" | awk -v name="$SERVICE_NAME" '$1 == name')"
    fi

    local count
    count="$(echo -n "$services" | grep -c '' || true)"
    if [[ "$count" -eq 1 ]]; then
        SERVICE_NAME="${services%% *}"
        INSTALL_DIR="${services#* }"
        log "Found existing service '$SERVICE_NAME' in $INSTALL_DIR"
        return
    fi
    if [[ "$count" -gt 1 ]]; then
        warn "Several Charon.Dns services found, use --dir/--service to choose one:"
        echo "$services" | sed 's/^/    /' >&2
    fi

    INSTALL_DIR="$(normalize_dir "$(ask "Installation directory" "$DEFAULT_DIR")")"
}

resolve_service_name() {
    [[ -n "$SERVICE_NAME" ]] && return

    if [[ -f "$INSTALL_DIR/$SERVICE_MARKER" ]]; then
        SERVICE_NAME="$(tr -d '[:space:]' <"$INSTALL_DIR/$SERVICE_MARKER")"
        [[ -n "$SERVICE_NAME" ]] && return
    fi

    local name dir
    while read -r name dir; do
        if [[ "$dir" == "$INSTALL_DIR" ]]; then
            SERVICE_NAME="$name"
            return
        fi
    done < <(find_existing_services)

    SERVICE_NAME="$DEFAULT_SERVICE"
}

latest_version_tag() {
    curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
        | grep -m1 '"tag_name"' \
        | sed -E 's/.*"tag_name"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/'
}

installed_version() {
    if [[ -x "$INSTALL_DIR/$APP_BINARY" ]]; then
        "$INSTALL_DIR/$APP_BINARY" --version 2>/dev/null | head -n1 | tr -d '[:space:]' || true
    fi
}

service_exists() {
    systemctl cat "$SERVICE_NAME.service" >/dev/null 2>&1
}

write_if_missing() {
    local path="$1"
    if [[ -e "$path" ]]; then
        log "Keeping existing $(basename "$path")"
        cat >/dev/null
    else
        cat >"$path"
        log "Created template $path"
    fi
}

write_templates() {
    if [[ -e "$INSTALL_DIR/settings.json" ]]; then
        log "Keeping existing configuration files"
        return
    fi

    write_if_missing "$INSTALL_DIR/settings.json" <<'EOF'
{
  "LogLevel": "Information",
  "FileLogLevel": "Information",
  "Server": {
    "MaxParallelRequestCount": 4,
    "SocketBufferSize": "2Mb",
    "ListenOn": [
      {
        "Address": "10.0.0.53",
        "Port": 53,
        "EnableIpV6": false
      }
    ],
    "DnsChain": {
      "ResolvingStrategy": "RoundRobin",
      "ResolvingConcurrencyLimit": 2,
      "SocketBufferSize": "512Kb",
      "DefaultServers": [
        "77.88.8.8",
        "77.88.8.1"
      ],
      "SecuredServers": [
        {
          "Ip": "1.1.1.1",
          "RouteThroughInterface": "wg0"
        }
      ]
    },
    "DnsRecords": {
      "A": []
    }
  },
  "Cache": {
    "Enabled": true,
    "TimeToLive": "00:05:00"
  },
  "UserAccessControl": {
    "Enabled": false,
    "InactivityThreshold": "00:30:00",
    "ControlledIps": []
  },
  "Routing": {
    "Period": "02:00:00",
    "BlockedHostNames": [
      "file:hosts_blacklist.txt"
    ],
    "Items": [
      {
        "InterfaceToRouteThrough": "wg0",
        "IpV4RoutingSubnet": 32,
        "IpV6RoutingSubnet": 96,
        "HostNameMatches": {
          "BySubstring": [],
          "ByDomainName": [
            "example.com",
            "file:other_hosts.txt"
          ]
        }
      }
    ]
  }
}
EOF

    write_if_missing "$INSTALL_DIR/hosts_blacklist.txt" <<'EOF'
# Blocked host names.
#
# Referenced from settings.json: "Routing" -> "BlockedHostNames" -> "file:hosts_blacklist.txt".
# Put one host name per line. Only exact host names are blocked (case-insensitive),
# subdomains must be listed separately.
# Empty lines and lines starting with '#' are ignored.
# Restart the service after editing this file.
#
# Examples:
# tracker.example.com
# ads.example.net
EOF

    write_if_missing "$INSTALL_DIR/other_hosts.txt" <<'EOF'
# Host names to route through the interface of the routing item that references this file.
#
# Referenced from settings.json: "Routing" -> "Items" -> "HostNameMatches" -> "ByDomainName" -> "file:other_hosts.txt".
# Put one domain name per line. The domain and all of its subdomains are matched
# (e.g. "example.com" also matches "www.example.com").
# Empty lines and lines starting with '#' are ignored.
# Restart the service after editing this file.
#
# Examples:
# example.com
# example.org
EOF
}

write_service_unit() {
    local unit_path="/etc/systemd/system/$SERVICE_NAME.service"
    cat >"$unit_path" <<EOF
[Unit]
Description=Charon.Dns - DNS server with dynamic traffic routing
Wants=network-online.target
After=network-online.target

[Service]
Type=simple
User=root
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/$APP_BINARY
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF
    log "Created systemd unit $unit_path"
    systemctl daemon-reload
    systemctl enable "$SERVICE_NAME.service" >/dev/null 2>&1
}

# Atomically replaces target with source (safe even for the currently running script).
replace_file() {
    local source="$1" target="$2" mode="$3"
    install -m "$mode" "$source" "$target.new"
    mv -f "$target.new" "$target"
}

main() {
    parse_args "$@"
    check_requirements

    local rid
    rid="$(detect_rid)"

    resolve_install_dir
    resolve_service_name
    log "Install directory: $INSTALL_DIR"
    log "Service name: $SERVICE_NAME"
    log "Architecture: $rid"

    local tag latest current
    tag="$(latest_version_tag)" || die "Failed to get the latest release from GitHub"
    [[ -n "$tag" ]] || die "Failed to get the latest release from GitHub"
    latest="${tag#v}"
    current="$(installed_version)"

    if [[ -n "$current" && "$current" == "$latest" && $FORCE -eq 0 ]]; then
        log "Charon.Dns $current is already up to date (use --force to reinstall)"
        exit 0
    fi

    if [[ -n "$current" ]]; then
        log "Updating Charon.Dns $current -> $latest"
    else
        log "Installing Charon.Dns $latest"
    fi

    TMP_DIR="$(mktemp -d)"
    trap cleanup EXIT

    local archive="$APP_BINARY.$rid.tar.gz"
    local url="https://github.com/$REPO/releases/download/$tag/$archive"
    log "Downloading $url"
    curl -fL --retry 3 --progress-bar -o "$TMP_DIR/$archive" "$url" || die "Failed to download $url"

    mkdir -p "$TMP_DIR/extracted"
    tar -xzf "$TMP_DIR/$archive" -C "$TMP_DIR/extracted"
    [[ -f "$TMP_DIR/extracted/$APP_BINARY" ]] || die "Archive does not contain $APP_BINARY"

    mkdir -p "$INSTALL_DIR"

    local was_active=0 had_service=0
    if service_exists; then
        had_service=1
        if systemctl is-active --quiet "$SERVICE_NAME.service"; then
            was_active=1
            log "Stopping $SERVICE_NAME"
            systemctl stop "$SERVICE_NAME.service"
        fi
    fi

    replace_file "$TMP_DIR/extracted/$APP_BINARY" "$INSTALL_DIR/$APP_BINARY" 755
    if [[ -f "$TMP_DIR/extracted/install.sh" ]]; then
        replace_file "$TMP_DIR/extracted/install.sh" "$INSTALL_DIR/install.sh" 755
    fi

    write_templates
    echo "$SERVICE_NAME" >"$INSTALL_DIR/$SERVICE_MARKER"

    if [[ $had_service -eq 0 ]]; then
        write_service_unit
    else
        log "Keeping existing systemd unit $SERVICE_NAME.service"
    fi

    if [[ $was_active -eq 1 ]]; then
        log "Starting $SERVICE_NAME"
        systemctl start "$SERVICE_NAME.service"
    fi

    echo
    log "Charon.Dns $latest installed to $INSTALL_DIR"
    if [[ $was_active -eq 0 ]]; then
        echo "    Edit $INSTALL_DIR/settings.json, then start the service:"
        echo "      sudo systemctl start $SERVICE_NAME"
    fi
    echo "    Logs:   journalctl -u $SERVICE_NAME -f   (or $INSTALL_DIR/logs)"
    echo "    Update: sudo $INSTALL_DIR/install.sh"
}

main "$@"
