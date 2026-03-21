using System.Text;
using Charon.Dns.Net;

namespace Charon.Dns.SystemCommands.Implementations;

public readonly struct RemoveIpRouteCommand<T> : ICommand where T : IIpNetwork<T>
{
    public required T Ip { get; init; }
    
    public void BuildCommand(StringBuilder commandBuilder)
    {
        commandBuilder.AppendFormat("ip ");
        commandBuilder.AppendFormat(Ip.IsIpV4 ? "-4" : "-6");
        commandBuilder.AppendFormat(" route del ");
        Ip.MinAddress.WriteToStringBuilder(commandBuilder);
    }
}
