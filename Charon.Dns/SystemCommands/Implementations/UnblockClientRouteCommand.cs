using System.Net;
using System.Text;

namespace Charon.Dns.SystemCommands.Implementations;

public readonly struct UnblockClientRouteCommand : ICommand
{
    public required IPAddress Ip { get; init; }

    public void BuildCommand(StringBuilder commandBuilder)
    {
        commandBuilder.Append($"ufw route delete deny from {Ip} comment '{Constants.UserAccessControlComment}'");
    }
}
