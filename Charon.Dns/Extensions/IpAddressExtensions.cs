using System.Net;
using System.Net.Sockets;

namespace Charon.Dns.Extensions;

public static class IpAddressExtensions
{
    extension(IPAddress baseAddress)
    {
        public int MaxPrefixLength => 
            baseAddress.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        
        public IPNetwork ToIPNetwork() => new IPNetwork(baseAddress, baseAddress.MaxPrefixLength); 
    }
}