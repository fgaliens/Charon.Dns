using System.Net;
using System.Text;

namespace Charon.Dns.SystemCommands.Implementations;

public readonly struct BlockClientRouteCommand : ICommand
{
    public required IPAddress Ip { get; init; }

    public void BuildCommand(StringBuilder commandBuilder)
    {
        commandBuilder.Append($"ufw route insert 1 deny from {Ip} comment '{Constants.UserAccessControlComment}'");
    }
}
