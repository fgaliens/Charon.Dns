using System.Collections.Concurrent;
using System.Net;
using Charon.Dns.Extensions;
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

    public async Task UnblockUser(IPAddress ip)
    {
        if (!settings.Enabled)
        {
            return;
        }
        
        var isCurrentlyBlocked = _blockedState.GetValueOrDefault(ip, true);
        if (isCurrentlyBlocked)
        {
            await SetBlocked(ip, shouldBlock: false);
        }
        
        activityTracker.RecordActivity(ip);
    }
    
    public async Task EvaluateAndEnforce()
    {
        if (!settings.Enabled)
        {
            return;
        }
            
        foreach (var ip in activityTracker.GetTrackedIps())
        {
            var lastSeen = activityTracker.GetLastSeen(ip);
            if (lastSeen is null)
            {
                continue;
            }

            var isStale = dateTimeProvider.UtcNow - lastSeen.Value > settings.InactivityThreshold;
            var isCurrentlyBlocked = _blockedState.GetValueOrDefault(ip, true);

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
        var isUfwActive = await UfwStatusParser.IsActive(
            commandRunner.ExecuteAndQuery(GetUfwStatusCommand.Instance));

        if (!isUfwActive && settings.Enabled)
        {
            throw new InvalidOperationException(
                "UserAccessControl is enabled in settings, but ufw is not active on this host.");
        }

        var count = 0;

        try
        {
            var deniedNetworks = UfwStatusParser.ExtractDeniedNetworks(
                commandRunner.ExecuteAndQuery(GetUfwStatusCommand.Instance),
                Constants.UserAccessControlComment);

            await foreach (var network in deniedNetworks)
            {
                count++;
                await commandRunner.Execute(new UnblockClientRouteCommand { Ip = network });
            }

            var allowedNetworks = UfwStatusParser.ExtractAllowedNetworks(
                commandRunner.ExecuteAndQuery(GetUfwStatusCommand.Instance),
                Constants.UserAccessControlComment);

            await foreach (var network in allowedNetworks)
            {
                count++;
                await commandRunner.Execute(new RevokeClientRouteCommand { Ip = network });
            }

            if (!settings.Enabled)
            {
                return;
            }

            foreach (var ipNetwork in settings.ControlledIps)
            {
                await commandRunner.Execute(new BlockClientRouteCommand { Ip = ipNetwork });
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
        var ipNetwork = ip.ToIPNetwork();
        logger.Information(
            "{Action} client {Ip} due to activity-based access control",
            shouldBlock ? "Blocking" : "Unblocking", ipNetwork);
        
        var success = shouldBlock
            ? await commandRunner.Execute(new RevokeClientRouteCommand { Ip = ipNetwork })
            : await commandRunner.Execute(new AllowClientRouteCommand { Ip = ipNetwork });

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
