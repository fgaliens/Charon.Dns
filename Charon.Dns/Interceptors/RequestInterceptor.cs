using Charon.Dns.AccessControl;
using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Settings;

namespace Charon.Dns.Interceptors;

public class RequestInterceptor(
    IClientActivityTracker activityTracker,
    UserAccessControlSettings settings)
    : IRequestInterceptor
{
    public Task Handle(
        IReadOnlyRequest request,
        RequestTrace trace,
        CancellationToken token = default)
    {
        if (!settings.Enabled)
        {
            return Task.CompletedTask;
        }

        var remoteIp = trace.RemoteEndPoint.Address;
        if (settings.ControlledIps.Any(network => network.Contains(remoteIp)))
        {
            activityTracker.RecordActivity(remoteIp);
        }

        return Task.CompletedTask;
    }

    async Task IAsyncObserver<OnRequestEventArgs>.OnEvent(OnRequestEventArgs eventArgs)
    {
        await Handle(eventArgs.Request, eventArgs.Trace);
    }

    Task IAsyncObserver<OnRequestEventArgs>.OnCompleted()
    {
        return Task.CompletedTask;
    }
}
