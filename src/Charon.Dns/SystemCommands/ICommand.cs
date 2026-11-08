using System.Text;

namespace Charon.Dns.SystemCommands;

public interface ICommand
{
    void BuildCommand(StringBuilder builder);
}