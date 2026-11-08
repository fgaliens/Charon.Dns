using System.Net;

namespace Charon.Dns.Lib.Server.Transports;

internal static class IpEndPointExtensions
{
    extension(IPEndPoint endPoint)
    {
        /// <summary>
        /// Dual mode sockets report IPv4 clients as IPv4-mapped IPv6 addresses,
        /// which would not match IPv4 subnets of access control and routing.
        /// </summary>
        public IPEndPoint Normalize()
        {
            if (!endPoint.Address.IsIPv4MappedToIPv6)
            {
                return endPoint;
            }

            var address = endPoint.Address.MapToIPv4();
            return new IPEndPoint(address, endPoint.Port);
        }
    }
}
