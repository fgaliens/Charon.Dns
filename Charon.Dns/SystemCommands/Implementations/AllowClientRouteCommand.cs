using System.Net;
using System.Text;

namespace Charon.Dns.SystemCommands.Implementations;

public readonly struct AllowClientRouteCommand : ICommand
{
    public required IPNetwork Ip { get; init; }

    public void BuildCommand(StringBuilder commandBuilder)
    {
        commandBuilder.Append($"ufw route insert 1 allow from {Ip} comment '{Constants.UserAccessControlComment}'");
    }
}
