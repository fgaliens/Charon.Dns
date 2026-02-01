using System.Text;
using Charon.Dns.Net;

namespace Charon.Dns.SystemCommands.Implementations;

public readonly struct AddIpV6RouteCommand : ICommand
{
    public required IpV6Network Ip { get; init; }

    public required string Interface { get; init; }

    public void BuildCommand(StringBuilder commandBuilder)
    {
        commandBuilder.AppendFormat("ip -6 route add ");
        Ip.WriteToStringBuilder(commandBuilder);
        commandBuilder.AppendFormat($" dev {Interface}");
    }
}