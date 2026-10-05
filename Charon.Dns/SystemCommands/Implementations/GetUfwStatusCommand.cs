using System.Text;

namespace Charon.Dns.SystemCommands.Implementations;

public readonly struct GetUfwStatusCommand : ICommand
{
    public static GetUfwStatusCommand Instance { get; } = new();

    public void BuildCommand(StringBuilder commandBuilder)
    {
        commandBuilder.Append("ufw status numbered");
    }
}
