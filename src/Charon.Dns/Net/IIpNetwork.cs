using System.Text;

namespace Charon.Dns.Net;

public interface IIpNetwork<T> : IEquatable<T> where T : IIpNetwork<T>
{
    T SubnetMask { get; }
    T MinAddress { get; }
    T MaxAddress { get; }
    bool IsIpV4 { get; }
    bool IsIpV6 { get; }
    void WriteToStringBuilder(StringBuilder stringBuilder);
}
