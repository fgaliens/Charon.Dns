using System.Text;

namespace Charon.Dns.SystemCommands.Implementations
{
    public readonly struct AddInterfaceForDnsCommand : ICommand
    {
        public required int InterfaceIndex { get; init; }
    
        public void BuildCommand(StringBuilder builder)
        {
            builder.AppendFormat($"ip link add name {Constants.InterfaceName}{InterfaceIndex} type dummy");
        }
    }
}
