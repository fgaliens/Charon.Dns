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
- on first install generates templates for `settings.json`, `routing.json`, `hosts_blacklist.txt` and `other_hosts.txt` and creates and enables a `charon-dns` systemd service (not started — edit `settings.json` and `routing.json` first, then run `sudo systemctl start charon-dns`);
- on update converts existing configuration files to the format of the new version (see [Configuration Versions](#configuration-versions)) and never modifies existing systemd units.

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

Commands:
- `settings migrate [--dir <path>]` — convert configuration files in the directory (default: current directory) to the format of this version, see [Configuration Versions](#configuration-versions).

Any configuration key (from `settings.json`, `routing.json` or `zones.json`) can also be overridden via command-line arguments, e.g.:

```bash
sudo ./Charon.Dns --Logging:Console=Warning --Logging:File=Warning
```

## Configuration

Charon.Dns is configured by editing the `settings.json` (server, upstream DNS, cache, access control), `routing.json` (traffic routing and blocking) and `zones.json` (local zones) files, located in the working directory. **Note:** The server must be restarted after any configuration changes.

When built in `Debug` configuration, the server loads `settings.debug.json`, `routing.debug.json`, `zones.debug.json` and `config.version.debug.json` instead — useful for local development without touching your production config. All debug files are git-ignored.

### settings.json Structure

```json
{
  "Logging": {
    "Console": "Information", // Log level for standard output
    "File": "Debug" // Log level for file output
  },
  "Server": {
    "SocketsPerEndpoint": 8, // Number of UDP sockets per "ListenOn" address sharing the load (SO_REUSEPORT)
    "SocketBufferSize": "2Mb", // UDP socket buffer size for incoming requests
    "ListenOn": [ // Addresses and ports the server listens on
      {
        "Address": "10.0.0.53",
        "Port": 53, // Optional, defaults to 53 for UdpAndTcp, 853 for DoT, 443 for DoH
        "EnableIPv6": false, // Use an IPv6 socket
        "Mode": "UdpAndTcp" // Optional: UdpAndTcp (default; UDP and TCP on the same port), DoT or DoH
      },
      {
        "Address": "10.0.0.53",
        "Mode": "DoT", // DNS over TLS (RFC 7858)
        "Certificate": { "Path": "cert.pem", "KeyPath": "key.pem" } // Required for DoT and DoH
      },
      {
        "Address": "10.0.0.53",
        "Mode": "DoH", // DNS over HTTPS (RFC 8484), HTTP/1.1 and HTTP/2
        "Certificate": { "Path": "cert.pfx", "Password": "secret" }, // PKCS#12 (.pfx, .p12) or PEM
        "Path": "/dns-query" // Optional, the path of the DoH endpoint
      }
    ]
  },
  "Upstream": {
    "ResolvingStrategy": "RoundRobin", // Resolving strategy (options: Random, Parallel, RoundRobin)
    "SocketBufferSize": "512Kb", // UDP socket buffer size for outgoing resolver connections
    "DefaultServers": [ // DNS servers for standard resolution
      {
        "Address": "77.88.8.8:53", // Port defaults to 53 for UdpAndTcp, 853 for DoT, 443 for DoH; add ":port" to override
        "Mode": "UdpAndTcp" // Optional: UdpAndTcp (default), DoT or DoH
      },
      {
        "Address": "8.8.8.8",
        "Mode": "DoH",
        "Host": "dns.google", // Optional, name to validate the certificate against (defaults to the address)
        "Path": "/dns-query" // Optional, DoH only
      }
    ],
    "RoutedServers": [ // DNS servers for routed domains (see routing.json), reached through the interface
      {
        "Address": "1.1.1.1:53", // Same fields as in "DefaultServers"
        "Interface": "wg0",
        "Mode": "UdpAndTcp"
      }
    ]
  },
  "Cache": {
    "Enabled": true, // Whether to cache resolved responses
    "Ttl": "00:05:00" // DNS cache TTL
  },
  "UserAccessControl": { // Activity-based access control via ufw (see Core Features)
    "Enabled": false,
    "InactivityThreshold": "00:30:00", // How long a client can be inactive before its access is revoked
    "ControlledSubnets": [ // Subnets whose clients are subject to this control
      "10.8.0.0/24"
    ]
  }
}
```

### routing.json Structure

```json
{
  "Routing": {
    "RouteIdleTimeout": "02:00:00", // A route is removed after it has not been used for this time
    "BlockedHosts": [ // Host names to block (exact match)
      "tracker.com",
      "file:hosts_blacklist.txt" // Can reference an external file with blocked hosts
    ],
    "Rules": [
      {
        "Interface": "wg0", // Interface to route traffic through
        "IPv4PrefixLength": 32, // Prefix length of added IPv4 routes
        "IPv6PrefixLength": 96, // Prefix length of added IPv6 routes
        "Match": { // Domain matching rules
          "Substrings": [
            "some-site" // Matches any domain containing "some-site"
          ],
          "Domains": [ // Matches domain and all subdomains
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

### Configuration Versions

The format version of the configuration files is stored in `config.version.json` next to them. The file is managed by Charon.Dns — do not edit it. A missing file (or one without a version) means version 0, the format of releases before 2.0.0.

On startup the server converts the configuration files to the format of its version, and `install.sh` does the same on update. To do it manually, run:

```bash
sudo ./Charon.Dns settings migrate --dir /etc/charon-dns
```

Before a file is changed, its previous content is saved next to it as `<file>.v<old version>.bak` (comments are not preserved in converted files, they are kept in the backups). If the files can't be converted, nothing is changed, and the server doesn't start until the problem is fixed.

Converting from 1.x (version 0):
- the `Server:DnsRecords:A` records are moved to `zones.json`; each name becomes a zone of its own (with the cache TTL), unless it belongs to an existing zone. **The server is authoritative for these zones now**: other record types and subdomains of these names are not forwarded upstream anymore;
- the `Routing` section is moved to `routing.json` (it must not be present in both files);
- fields are renamed to the names above (`LogLevel` → `Logging:Console`, `DnsChain` → `Upstream`, `SecuredServers` → `RoutedServers`, `Items` → `Rules` etc.);
- `ListenOn` items marked `"DebugOnly": true` are removed (1.x didn't listen on them in release builds), `ResolvingConcurrencyLimit` is removed (it had no effect);
- `"Mode": "UdpAndTcp"` is added to `ListenOn` and `RoutedServers` items, and `DefaultServers` given as strings (`"77.88.8.8"`) become objects (`{ "Address": "77.88.8.8", "Mode": "UdpAndTcp" }`). **Listeners accept DNS over TCP on the same port now**, in addition to UDP.

## Requirements

- Linux-based operating system
- Root privileges (for route manipulation)
