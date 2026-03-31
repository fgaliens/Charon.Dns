namespace Charon.Dns.Lib.Server;

public interface IRequestCounter
{
    ulong RequestsCount { get; }
    ulong Increment();
}
