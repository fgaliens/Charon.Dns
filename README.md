# Charon.Dns

**Charon.Dns** is a DNS server with the ability to dynamically manage traffic routes on your Linux server.

Charon.Dns.Lib library is based on the [kapetan/dns](https://github.com/kapetan/dns) library.

## The Problem

Imagine you have two gateways on host:
- A default gateway
- A VPN gateway

You want most traffic to go through your default gateway, but all traffic to `example.com` should be routed through the VPN. Normally, you'd use `ip route add ...` to accomplish this. But what if `example.com` has multiple IP addresses that can change over time or you need to route many sites? Manually managing these routes becomes impractical.

## The Solution

Charon.Dns solves this by automatically adding routes whenever it receives DNS queries. When a client requests any type of DNS record (A, AAAA, MX, etc.), Charon.Dns:

1. Detects the domain being queried
2. Dynamically adds a route for that domain's IP address(es) through your specified gateway (e.g., VPN)
3. Returns the DNS response to the client

## Core Features

### 🚦 Traffic Routing Management
Automatically adds Linux routes for domains as they are queried, enabling selective routing through specific gateways.

### 🔍 DNS Resolution
Resolves any query type (A, AAAA, CNAME, MX, SOA, SRV, TXT, etc.) by forwarding it to upstream DNS servers. Can also serve local zones authoritatively (A, AAAA, CNAME, MX, NS, PTR, SRV, TXT) without a round trip upstream (see `zones.json` in Configuration).

### 🚫 Domain Blocking
Blocks unwanted domains by adding them to a blacklist — perfect for blocking analytics trackers, advertising domains, or any other undesirable destinations.

### 🔐 Activity-Based Access Control
Tracks which client IPs are actively resolving DNS through the server and uses `ufw` to automatically revoke network access for clients that go inactive longer than a configured threshold, restoring it once they're active again — a kill switch for VPN-only clients.

## How It Works

With Charon.Dns running, you can configure routing rules that say: "When anyone looks up `example.com`, route traffic to its IPs through the VPN." The server handles dynamic IP changes automatically — no need to manually update routes when IP addresses change.

Additionally, you can:
- Block analytics and tracking domains so they never resolve
- Use Charon.Dns as a standard DNS resolver for lookups of any record type
- Automatically cut off network access for clients that stop using the VPN, and restore it once they're active again

## Installation

Install (or update) the latest release as a systemd service:

```bash
curl -fsSL https://raw.githubusercontent.com/fgaliens/DNS/master/install.sh | sudo bash -s -- --dir /etc/charon-dns
```

The installer:
- detects the system architecture (`x86_64` and `aarch64`/`arm64`, e.g. Raspberry Pi with a 64-bit OS) and downloads the matching build from the latest GitHub release;
- installs the `Charon.Dns` binary and the `install.sh` script into the installation directory;
- on first install generates templates for `settings.json`, `hosts_blacklist.txt` and `other_hosts.txt` and creates and enables a `charon-dns` systemd service (not started — edit `settings.json` first, then run `sudo systemctl start charon-dns`);
- never modifies existing configuration files or existing systemd units.

To update an existing installation, run the script that lies next to the application:

```bash
sudo /etc/charon-dns/install.sh
```

The installation directory and the service name are detected automatically: from the script location, the `.service-name` file in the installation directory or existing systemd units running `Charon.Dns`. If the service is running, it is stopped for the update and started again afterwards.

Installer options:

| Option | Description |
|---|---|
| `-d`, `--dir <path>` | Installation directory (asked interactively if not set; default `/etc/charon-dns`) |
| `-s`, `--service <name>` | systemd service name (default: detected, or `charon-dns`) |
| `-f`, `--force` | Reinstall even if the latest version is already installed |
| `-h`, `--help` | Show help |

Useful commands:

```bash
sudo systemctl start|stop|restart charon-dns
journalctl -u charon-dns -f   # logs are also written to <install dir>/logs
```

## Usage

Run the server (requires root privileges on Linux for route management) from the directory containing `settings.json`:

```bash
sudo ./Charon.Dns
```

Command-line options:
- `-v`, `--version` — print the application version and exit.

Any `settings.json` key can also be overridden via command-line arguments, e.g.:

```bash
sudo ./Charon.Dns --LogLevel=Warning --FileLogLevel=Warning
```

## Configuration

Charon.Dns is configured by editing the `settings.json` file. **Note:** The server must be restarted after any configuration changes.

When built in `Debug` configuration, the server loads `settings.debug.json` and `zones.debug.json` instead — useful for local development without touching your production config. Both debug files are git-ignored.

### settings.json Structure

```json
{
  "LogLevel": "Information", // Log level for standard output
  "FileLogLevel": "Debug", // Log level for file output
  "Server": {
    "MaxParallelRequestCount": 8, // Number of parallel UDP listeners sharing the load (SO_REUSEPORT)
    "SocketBufferSize": "2Mb", // UDP socket buffer size for incoming requests
    "ListenOn": [ // Addresses and ports the server listens on
      {
        "Address": "10.0.0.53",
        "Port": 53,
        "EnableIpV6": false // Also accept IPv6 traffic on this socket
      }
    ],
    "DnsChain": {
      "ResolvingStrategy": "RoundRobin", // Resolving strategy (options: Random, Parallel, RoundRobin)
      "ResolvingConcurrencyLimit": 2, // Maximum number of outgoing connections to external DNS servers
      "SocketBufferSize": "512Kb", // UDP socket buffer size for outgoing resolver connections
      "DefaultServers": [ // DNS servers for standard resolution; port defaults to 53, add ":port" to override
        "77.88.8.8:53",
        "77.88.8.1"
      ],
      "SecuredServers": [ // DNS servers and interfaces for domains requiring special routing
        {
          "Ip": "1.1.1.1:53", // Same ":port" syntax is supported here
          "RouteThroughInterface": "wg0"
        },
        {
          "Ip": "8.8.8.8",
          "RouteThroughInterface": "wg0"
        }
      ]
    }
  },
  "Cache": {
    "Enabled": true, // Whether to cache resolved responses
    "TimeToLive": "00:05:00" // DNS cache TTL
  },
  "UserAccessControl": { // Activity-based access control via ufw (see Core Features)
    "Enabled": false,
    "InactivityThreshold": "00:30:00", // How long a client can be inactive before its access is revoked
    "ControlledIps": [ // Subnets whose clients are subject to this control
      "10.8.0.0/24"
    ]
  },
  "Routing": {
    "Period": "02:00:00", // Duration for which routes are added
    "BlockedHostNames": [ // List of domains to block
      "tracker.com",
      "file:hosts_blacklist.txt" // Can reference an external file with blocked hosts
    ],
    "Items": [
      {
        "InterfaceToRouteThrough": "wg0", // Interface to route traffic through
        "IpV4RoutingSubnet": 32, // Subnet size for added IPv4 routes
        "IpV6RoutingSubnet": 96, // Subnet size for added IPv6 routes
        "HostNameMatches": { // Domain matching rules
          "BySubstring": [
            "some-site" // Matches any domain containing "some-site"
          ],
          "ByDomainName": [ // Matches domain and all subdomains
            "example.com",
            "file:other-hosts.txt"
          ]
        }
      }
    ]
  }
}
```

### zones.json Structure

Local zones live in `zones.json` next to `settings.json` and are loaded the same way (`zones.debug.json` in `Debug`). `install.sh` creates it if missing. The server is authoritative for every name inside a zone: missing names get `NXDOMAIN`, missing record types get an empty answer (both with the zone `SOA` in the authority section), and such queries are never forwarded upstream. `CNAME` records pointing inside local zones are followed.

```json
{
  "DefaultTtl": "00:05:00", // TTL for records without their own "Ttl"
  "Zones": [
    {
      "Origin": "my.home", // Zone name
      "Ttl": "00:05:00", // Optional, overrides DefaultTtl for this zone
      "Soa": { // Optional, these are the defaults
        "PrimaryNameServer": "ns",
        "ResponsibleMailbox": "hostmaster",
        "Serial": 1,
        "Refresh": "01:00:00",
        "Retry": "00:10:00",
        "Expire": "1.00:00:00",
        "MinimumTtl": "00:05:00" // Defaults to the zone TTL
      },
      "Records": [
        { "Name": "@", "Type": "A", "Value": "10.10.1.25" },
        { "Name": "@", "Type": "A", "Value": "192.168.1.1", "ResolveOnlyIfRequestCameFrom": "10.10.1.1" }, // Returned only to this client
        { "Name": "@", "Type": "AAAA", "Value": "fd00::25" },
        { "Name": "nas", "Type": "CNAME", "Value": "@" },
        { "Name": "@", "Type": "MX", "Value": "10 mail" }, // "<preference> <exchange>"
        { "Name": "@", "Type": "TXT", "Value": "v=spf1 -all" },
        { "Name": "_http._tcp", "Type": "SRV", "Value": "0 5 80 nas", "Ttl": "01:00:00" } // "<priority> <weight> <port> <target>"
      ]
    }
  ]
}
```

Names in `Name` and in domain parts of `Value`: `@` is the zone origin, a name ending with a dot (`example.com.`) is absolute, any other name is relative to the origin (`nas` → `nas.my.home`).

The legacy `Server:DnsRecords:A` section of `settings.json` is still read, but deprecated: its records are answered non-authoritatively and a warning is logged on startup.

## Requirements

- Linux-based operating system
- Root privileges (for route manipulation)
