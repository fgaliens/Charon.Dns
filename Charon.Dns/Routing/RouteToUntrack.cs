using System.Diagnostics.CodeAnalysis;

namespace Charon.Dns.Routing;

public abstract class RouteToUntrack<T> : IDisposable
{
    [MemberNotNullWhen(true, nameof(Route))] 
    public required bool Found { get; init; }
    public required T? Route { get; init; }

    public abstract void Dispose();
}
