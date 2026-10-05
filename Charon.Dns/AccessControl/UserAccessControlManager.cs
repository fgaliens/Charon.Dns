using System.Collections.Concurrent;
using System.Net;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Settings;
using Charon.Dns.SystemCommands;
using Charon.Dns.SystemCommands.Implementations;
using Charon.Dns.Utils;
using Serilog;

namespace Charon.Dns.AccessControl;

public class UserAccessControlManager(
    ICommandRunner commandRunner,
    IClientActivityTracker activityTracker,
    UserAccessControlSettings settings,
    IDateTimeProvider dateTimeProvider,
    ILogger logger)
    : IUserAccessControlManager
{
    private readonly ConcurrentDictionary<IPAddress, bool> _blockedState = new();

    public async Task EvaluateAndEnforce()
    {
        foreach (var ip in activityTracker.GetTrackedIps())
        {
            var lastSeen = activityTracker.GetLastSeen(ip);
            if (lastSeen is null)
            {
                continue;
            }

            var isStale = dateTimeProvider.UtcNow - lastSeen.Value > settings.InactivityThreshold;
            var isCurrentlyBlocked = _blockedState.GetValueOrDefault(ip);

            if (isStale && !isCurrentlyBlocked)
            {
                await SetBlocked(ip, shouldBlock: true);
            }
            else if (!isStale && isCurrentlyBlocked)
            {
                await SetBlocked(ip, shouldBlock: false);
            }
        }
    }

    public async Task RevertAllTaggedRules()
    {
        var trace = RequestTrace.Empty;

        var isUfwActive = await UfwStatusParser.IsActive(
            commandRunner.ExecuteAndQuery(GetUfwStatusCommand.Instance, trace));

        if (!isUfwActive && settings.Enabled)
        {
            throw new InvalidOperationException(
                "UserAccessControl is enabled in settings, but ufw is not active on this host.");
        }

        var count = 0;

        try
        {
            var lines = commandRunner.ExecuteAndQuery(GetUfwStatusCommand.Instance, trace);
            var blockedIps = UfwStatusParser.ExtractBlockedIps(lines, Constants.UserAccessControlComment);

            await foreach (var ip in blockedIps)
            {
                count++;
                await commandRunner.Execute(new UnblockClientRouteCommand { Ip = ip }, trace);
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to query ufw status while reverting tagged rules");
        }

        logger.Information(
            "Found {Count} ufw rule(s) tagged '{Tag}' on startup; removed unconditionally",
            count, Constants.UserAccessControlComment);

        _blockedState.Clear();
    }

    private async Task SetBlocked(IPAddress ip, bool shouldBlock)
    {
        var trace = RequestTrace.Empty;
        logger.Information(
            "{Action} client {Ip} due to activity-based access control",
            shouldBlock ? "Blocking" : "Unblocking", ip);

        var success = shouldBlock
            ? await commandRunner.Execute(new BlockClientRouteCommand { Ip = ip }, trace)
            : await commandRunner.Execute(new UnblockClientRouteCommand { Ip = ip }, trace);

        if (success)
        {
            _blockedState[ip] = shouldBlock;
        }
        else
        {
            logger.Warning("Failed to {Action} client {Ip}", shouldBlock ? "block" : "unblock", ip);
        }
    }
}
