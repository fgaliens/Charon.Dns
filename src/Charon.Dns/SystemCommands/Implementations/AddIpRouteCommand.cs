using System.Text;
using Charon.Dns.Net;

namespace Charon.Dns.SystemCommands.Implementations;

public readonly struct AddIpRouteCommand<T> : ICommand where T : IIpNetwork<T>
{
    public required T Ip { get; init; }

    public required string Interface { get; init; }

    public void BuildCommand(StringBuilder commandBuilder)
    {
        commandBuilder.AppendFormat("ip ");
        commandBuilder.AppendFormat(Ip.IsIpV4 ? "-4" : "-6");
        commandBuilder.AppendFormat(" route add ");
        Ip.WriteToStringBuilder(commandBuilder);
        commandBuilder.AppendFormat($" dev {Interface}");
    }
}
