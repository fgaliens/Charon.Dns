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
Resolves any query type (A, AAAA, CNAME, MX, SOA, SRV, TXT, etc.) by forwarding it to upstream DNS servers. Can also answer directly from a small set of locally configured static records — currently `A` only — without a round trip upstream (see `DnsRecords` in Configuration).

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

## Usage

Run the server (requires root privileges on Linux for route management):

```bash
dotnet Charon.Dns.dll
```

Command-line options:
- `-v`, `--version` — print the application version and exit.

Any `settings.json` key can also be overridden via command-line arguments, e.g.:

```bash
dotnet Charon.Dns.dll --LogLevel=Warning --FileLogLevel=Warning
```

## Configuration

Charon.Dns is configured by editing the `settings.json` file. **Note:** The server must be restarted after any configuration changes.

When built in `Debug` configuration, the server loads `settings.debug.json` instead — useful for local development without touching your production config.

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
      "DefaultServers": [ // DNS servers for standard resolution
        "77.88.8.8",
        "77.88.8.1"
      ],
      "SecuredServers": [ // DNS servers and interfaces for domains requiring special routing
        {
          "Ip": "1.1.1.1",
          "RouteThroughInterface": "wg0"
        },
        {
          "Ip": "8.8.8.8",
          "RouteThroughInterface": "wg0"
        }
      ]
    },
    "DnsRecords": { // Local static records the server answers directly, without querying upstream
      "A": [ // Only the A record type is supported here
        {
          "Name": "my.home",
          "Address": "10.10.1.25"
        },
        {
          "Name": "my.home",
          "Address": "192.168.1.1",
          "ResolveOnlyIfRequestCameFrom": "10.10.1.1" // Conditional response based on source IP
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

## Requirements

- Linux-based operating system
- Root privileges (for route manipulation)
