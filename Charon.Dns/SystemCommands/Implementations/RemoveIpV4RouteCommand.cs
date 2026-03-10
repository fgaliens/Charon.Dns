using System.Text;
using Charon.Dns.Net;

namespace Charon.Dns.SystemCommands.Implementations;

public readonly struct RemoveIpV4RouteCommand : ICommand
{
    public required IpV4Network Ip { get; init; }

    public void BuildCommand(StringBuilder commandBuilder)
    {
        commandBuilder.AppendFormat("ip -4 route del ");
        Ip.MinAddress.WriteToStringBuilder(commandBuilder);
    }
}
