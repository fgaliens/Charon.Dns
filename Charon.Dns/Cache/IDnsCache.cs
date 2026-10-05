using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Cache;

public interface IDnsCache
{
    void AddResponse(
        IReadOnlyRequest request,
        IReadOnlyResponse response,
        RequestTrace trace);
    bool TryGetResponse(
        IReadOnlyRequest request,
        RequestTrace trace,
        [NotNullWhen(true)] out IResponse? response);
    void RemoveOutdatedResponses();
}
