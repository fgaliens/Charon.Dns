# Charon.Dns

**Charon.Dns** is a DNS server with the ability to dynamically manage traffic routes on your Linux server.

## The Problem

Imagine you have two gateways:
- A default gateway
- A VPN gateway

You want most traffic to go through your default gateway, but all traffic to `example.com` should be routed through the VPN. Normally, you'd use `ip route add ...` to accomplish this. But what if `example.com` has multiple IP addresses that can change over time? Manually managing these routes becomes impractical.

## The Solution

Charon.Dns solves this by automatically adding routes whenever it receives DNS queries. When a client requests any type of DNS record (A, AAAA, MX, etc.), Charon.Dns:

1. Detects the domain being queried
2. Dynamically adds a route for that domain's IP address(es) through your specified gateway (e.g., VPN)
3. Returns the DNS response to the client

## Core Features

### 🚦 Traffic Routing Management
Automatically adds Linux routes for domains as they are queried, enabling selective routing through specific gateways.

### 🔍 DNS Resolution
Performs standard DNS resolution with support for A records (with more record types planned for future releases).

### 🚫 Domain Blocking
Blocks unwanted domains by adding them to a blacklist — perfect for blocking analytics trackers, advertising domains, or any other undesirable destinations.

## How It Works

With Charon.Dns running, you can configure routing rules that say: "When anyone looks up `example.com`, route traffic to its IPs through the VPN." The server handles dynamic IP changes automatically — no need to manually update routes when IP addresses change.

Additionally, you can:
- Block analytics and tracking domains so they never resolve
- Use Charon.Dns as a standard DNS resolver for A record lookups

## Use Cases

- Selective VPN routing based on domain names
- Split tunneling for specific services
- Dynamic routing for domains with frequently changing IP addresses
- Ad and tracker blocking at the DNS level
- Local DNS server with custom filtering rules

## Requirements

- Linux-based operating system
- Root privileges (for route manipulation)
