# Charon.Dns

**Charon.Dns** is a DNS server for Linux that routes traffic by domain name. Tell it which domains should go through a specific interface (e.g. a VPN), and it adds the routes automatically whenever those domains are resolved.

## Why

Say your server has two ways out to the internet: the default gateway and a VPN. You want most traffic to go directly, but `example.com` should go through the VPN.

The usual answer is `ip route add ...`, but that only works with IP addresses. Sites have many addresses, they change over time, and keeping routes for dozens of sites up to date by hand is impractical.

Charon.Dns solves this at the DNS level. When a client resolves `example.com`, the server:

1. sees that the domain matches a routing rule;
2. resolves it and adds routes for the returned addresses through the configured interface;
3. returns the answer to the client.

Address changes are picked up automatically, and routes that are no longer used are removed after a while.

## Features

- **Domain-based routing** — route traffic for chosen domains (and their subdomains) through a specific interface.
- **DNS forwarding** — resolves any record type via upstream servers over plain DNS (UDP/TCP), DNS over TLS or DNS over HTTPS, with caching.
- **Encrypted DNS for clients** — accepts queries over UDP/TCP, DNS over TLS (DoT) and DNS over HTTPS (DoH).
- **Domain blocking** — block ads, trackers or any other hosts.
- **Local zones** — serve your own records (`nas.my.home`, etc.) without going upstream.
- **VPN kill switch** — uses `ufw` to cut network access for clients that stop using the server's DNS, and restores it when they come back.

## Requirements

- Linux, `x86_64` or `arm64` (e.g. Raspberry Pi with a 64-bit OS)
- root privileges (needed to manage routes)
- systemd (for the installer)
- `ufw` — only for the kill switch

## Installation

Install the latest release as a systemd service:

```bash
curl -fsSL https://raw.githubusercontent.com/fgaliens/DNS/master/install.sh | sudo bash -s -- --dir /etc/charon-dns
```

The installer downloads the build for your architecture, generates configuration templates and creates the `charon-dns` service. The service is **not started** on first install — configure it first (see [Configuration](#configuration)), then:

```bash
sudo systemctl start charon-dns
```

### Updating

Run the installer that lies next to the application:

```bash
sudo /etc/charon-dns/install.sh
```

The installation directory and the service name are detected automatically. A running service is stopped for the update and started again. Existing configuration files are kept and converted to the format of the new version if needed (see [Upgrading from 1.x](#upgrading-from-1x)).

### Installer options

| Option | Description |
|---|---|
| `-d`, `--dir <path>` | Installation directory (asked interactively if not set; default `/etc/charon-dns`) |
| `-s`, `--service <name>` | systemd service name (default: detected, or `charon-dns`) |
| `-f`, `--force` | Reinstall even if the latest version is already installed |
| `-h`, `--help` | Show help |

### Managing the service

```bash
sudo systemctl start|stop|restart charon-dns
journalctl -u charon-dns -f   # logs are also written to /etc/charon-dns/logs
```

## Configuration

All configuration lives in the installation directory:

| File | Purpose |
|---|---|
| `settings.json` | Listening addresses, upstream DNS servers, cache, kill switch |
| `routing.json` | Routing rules and blocked hosts |
| `zones.json` | Local DNS zones |
| `blocked_hosts.txt`, `other_domains.txt` | Host lists referenced from `routing.json` |

> Restart the service after changing any of these files: `sudo systemctl restart charon-dns`.

Files support `//` comments. Time values use the `hh:mm:ss` format (`1.00:00:00` is one day).

### settings.json

```jsonc
{
  "Logging": {
    "Console": "Information", // Log level for the console / journal
    "File": "Information"     // Log level for files in logs/
  },
  "Server": {
    "ListenOn": [ // Where clients send their queries
      {
        "Address": "10.0.0.53",
        "Port": 53,          // Optional: 53 for UdpAndTcp, 853 for DoT, 443 for DoH
        "EnableIPv6": false, // Set to true for an IPv6 address
        "Mode": "UdpAndTcp"  // UdpAndTcp (default), DoT or DoH
      }
    ]
  },
  "Upstream": {
    "ResolvingStrategy": "RoundRobin", // How to pick a server: Random, Parallel or RoundRobin
    "DefaultServers": [ // Servers for ordinary domains
      { "Address": "77.88.8.8", "Mode": "UdpAndTcp" },
      { "Address": "8.8.8.8", "Mode": "DoH", "Host": "dns.google" }
    ],
    "RoutedServers": [ // Servers for routed domains, reached through the given interface
      { "Address": "1.1.1.1", "Interface": "wg0", "Mode": "UdpAndTcp" }
    ]
  },
  "Cache": {
    "Enabled": true,
    "Ttl": "00:05:00"
  },
  "UserAccessControl": { // Kill switch, see the how-to below
    "Enabled": false,
    "InactivityThreshold": "00:30:00",
    "ControlledSubnets": []
  }
}
```

Upstream server fields:

| Field | Description |
|---|---|
| `Address` | IP address, optionally with a port (`1.1.1.1:5353`) |
| `Mode` | `UdpAndTcp` (default), `DoT` or `DoH` |
| `Host` | DoT/DoH only: name to validate the server certificate against (defaults to the address) |
| `Path` | DoH only: endpoint path (default `/dns-query`) |
| `Interface` | `RoutedServers` only: interface to reach the server through |

Resolving routed domains through `RoutedServers` means the DNS queries themselves also go through the VPN, and the answers are the ones the VPN side sees.

### routing.json

```jsonc
{
  "Routing": {
    "RouteIdleTimeout": "02:00:00", // Remove a route after it hasn't been used for this time
    "BlockedHosts": [ // Exact host names to block
      "tracker.example.com",
      "file:blocked_hosts.txt"
    ],
    "Rules": [
      {
        "Interface": "wg0",      // Interface to route matching traffic through
        "IPv4PrefixLength": 32,  // Size of added IPv4 routes
        "IPv6PrefixLength": 96,  // Size of added IPv6 routes
        "Match": {
          "Domains": [           // The domain and all its subdomains
            "example.com",
            "file:other_domains.txt"
          ],
          "Substrings": [        // Any domain containing the string
            "some-site"
          ]
        }
      }
    ]
  }
}
```

`file:<path>` includes host names from a text file: one name per line, empty lines and lines starting with `#` are ignored.

### zones.json

```jsonc
{
  "DefaultTtl": "00:05:00",
  "Zones": [
    {
      "Origin": "my.home",
      "Records": [
        { "Name": "@",   "Type": "A",     "Value": "10.10.1.25" },
        { "Name": "@",   "Type": "AAAA",  "Value": "fd00::25" },
        { "Name": "nas", "Type": "CNAME", "Value": "@" },
        { "Name": "@",   "Type": "MX",    "Value": "10 mail" },
        { "Name": "@",   "Type": "TXT",   "Value": "v=spf1 -all" },
        { "Name": "_http._tcp", "Type": "SRV", "Value": "0 5 80 nas", "Ttl": "01:00:00" }
      ]
    }
  ]
}
```

- Supported record types: `A`, `AAAA`, `CNAME`, `MX`, `NS`, `PTR`, `SRV`, `TXT`.
- Names: `@` is the zone itself, `nas` means `nas.my.home`, a name ending with a dot (`example.com.`) is absolute.
- The server is authoritative for the whole zone: names that are not listed return `NXDOMAIN` and are never forwarded upstream.
- A zone can optionally set its own `Ttl` and `Soa` parameters; a record can set its own `Ttl`.

### Command line

```bash
Charon.Dns --version                        # print the version
Charon.Dns settings migrate [--dir <path>]  # convert configuration files to the current format
```

Any setting can be overridden from the command line, e.g. `--Logging:Console=Warning`.

## How-to

### Point clients to the server

Set the server address from `Server:ListenOn` (e.g. `10.0.0.53`) as the DNS server on your clients — in the DHCP settings of your router, in the VPN client configuration (`DNS = 10.0.0.53` for WireGuard) or in the network settings of a single device.

Check that it answers:

```bash
dig @10.0.0.53 example.com
```

### Route a site through the VPN

1. Add the domain to a rule in `routing.json` (or to `other_domains.txt`):

   ```jsonc
   "Rules": [
     {
       "Interface": "wg0",
       "IPv4PrefixLength": 32,
       "IPv6PrefixLength": 96,
       "Match": { "Domains": ["example.com"] }
     }
   ]
   ```

2. Optionally add a server to `Upstream:RoutedServers` with the same `Interface`, so these domains are also resolved through the VPN.
3. Restart the service and resolve the domain from a client.
4. Check that the route was added:

   ```bash
   ip route | grep wg0
   ```

All subdomains (`www.example.com`, `cdn.example.com`) are routed too. If a site loads resources from other domains, add them as well.

For several VPNs, add a rule per interface.

### Block ads and trackers

Add host names to `blocked_hosts.txt`, one per line, and restart the service. Only exact names are blocked — list subdomains separately. The file can be filled from public blocklists as long as they contain plain host names (strip `0.0.0.0` prefixes from hosts-file style lists).

### Add local names for your devices

Create a zone in `zones.json`:

```jsonc
{
  "Zones": [
    {
      "Origin": "my.home",
      "Records": [
        { "Name": "router", "Type": "A", "Value": "192.168.1.1" },
        { "Name": "nas",    "Type": "A", "Value": "192.168.1.10" }
      ]
    }
  ]
}
```

After a restart `nas.my.home` resolves to `192.168.1.10`.

To return a different address depending on the client, add `"ResolveOnlyIfRequestCameFrom": "<client IP>"` to a record — it will only be returned to that client.

### Use encrypted DNS

**Upstream.** Switch servers in `Upstream` to DoT or DoH so your ISP can't see or alter your DNS queries:

```jsonc
"DefaultServers": [
  { "Address": "1.1.1.1", "Mode": "DoT", "Host": "one.one.one.one" },
  { "Address": "8.8.8.8", "Mode": "DoH", "Host": "dns.google" }
]
```

**For clients.** Add DoT and/or DoH listeners to `Server:ListenOn`. They need a certificate — PEM (`Path` + `KeyPath`) or PKCS#12 (`Path` + optional `Password`):

```jsonc
"ListenOn": [
  { "Address": "10.0.0.53", "Mode": "UdpAndTcp" },
  {
    "Address": "10.0.0.53",
    "Mode": "DoT",
    "Certificate": { "Path": "cert.pem", "KeyPath": "key.pem" }
  },
  {
    "Address": "10.0.0.53",
    "Mode": "DoH",
    "Certificate": { "Path": "cert.pfx", "Password": "secret" },
    "Path": "/dns-query"
  }
]
```

Clients connect to `tls://<server>:853` for DoT and `https://<server>/dns-query` for DoH. The certificate must be trusted by the clients and issued for the name they connect to.

### Enable the VPN kill switch

If clients connect to your server through a VPN, the kill switch makes sure they can't use the network without also using Charon.Dns. A client that hasn't sent a DNS query for longer than `InactivityThreshold` loses network access via `ufw`, and gets it back with its next query.

1. Make sure `ufw` is installed and active: `sudo ufw status`.
2. Enable the feature in `settings.json`, listing the VPN client subnet:

   ```jsonc
   "UserAccessControl": {
     "Enabled": true,
     "InactivityThreshold": "00:30:00",
     "ControlledSubnets": ["10.8.0.0/24"]
   }
   ```

3. Restart the service.

## Upgrading from 1.x

Version 2.0 changed the configuration format. The installer converts existing files automatically on update; to do it manually, run:

```bash
sudo /etc/charon-dns/Charon.Dns settings migrate --dir /etc/charon-dns
```

The previous versions of changed files are saved next to them as `<file>.v0.bak`. If the conversion fails, nothing is changed and the server won't start until the problem is fixed.

Behavior changes to be aware of:

- **Static records from `Server:DnsRecords` are moved to `zones.json`, and the server is now authoritative for them**: other record types and subdomains of these names are no longer forwarded upstream.
- **The `Routing` section is moved to `routing.json`.**
- **Listeners accept DNS over TCP on the same port**, in addition to UDP.

## Credits

The DNS protocol library is based on [kapetan/dns](https://github.com/kapetan/dns).
