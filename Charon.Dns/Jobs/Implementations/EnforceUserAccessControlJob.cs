using Charon.Dns.AccessControl;
using Charon.Dns.Settings;

namespace Charon.Dns.Jobs.Implementations;

public class EnforceUserAccessControlJob(
    IUserAccessControlManager accessControlManager,
    UserAccessControlSettings settings)
    : IJob
{
    public TimeSpan Period { get; } = settings.Enabled ? settings.InactivityThreshold / 4 : TimeSpan.Zero;

    public async Task Execute()
    {
        await accessControlManager.EvaluateAndEnforce();
    }
}
