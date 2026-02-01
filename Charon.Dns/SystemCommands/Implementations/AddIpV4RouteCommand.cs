using System.Text;
using Charon.Dns.Net;

namespace Charon.Dns.SystemCommands.Implementations;

public readonly struct AddIpV4RouteCommand : ICommand
{
    public required IpV4Network Ip { get; init; }

    public required string Interface { get; init; }

    public void BuildCommand(StringBuilder commandBuilder)
    {
        commandBuilder.AppendFormat("ip -4 route add ");
        Ip.WriteToStringBuilder(commandBuilder);
        commandBuilder.AppendFormat($" dev {Interface}");
    }
}