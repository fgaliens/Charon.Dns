using System.Net;
using System.Text;

namespace Charon.Dns.SystemCommands.Implementations
{
    public readonly struct SetIpForDnsInterfaceCommand : ICommand
    {
        public required IPAddress InterfaceAddress { get; init; }
        public required int InterfaceIndex { get; init; }
    
        public void BuildCommand(StringBuilder builder)
        {
            builder.AppendFormat($"ip addr add {InterfaceAddress} dev {Constants.InterfaceName}{InterfaceIndex}");
        }
    }
}
