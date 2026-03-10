using System.Text;
using Charon.Dns.Net;

namespace Charon.Dns.SystemCommands.Implementations;

public readonly struct RemoveIpV6RouteCommand : ICommand
{
    public required IpV6Network Ip { get; init; }
    
    public void BuildCommand(StringBuilder commandBuilder)
    {
        commandBuilder.AppendFormat("ip -6 route del ");
        Ip.MinAddress.WriteToStringBuilder(commandBuilder);
    }
}
