using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.AccessControl;
using Charon.Dns.Settings;
using Charon.Dns.SystemCommands;
using Charon.Dns.SystemCommands.Implementations;
using Charon.Dns.Tests.Utils.Mock;
using Charon.Dns.Utils;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Serilog;
using Xunit;

namespace Charon.Dns.Tests.AccessControl;

[TestSubject(typeof(UserAccessControlManager))]
public class UserAccessControlManagerTest
{
    private static readonly TimeSpan DefaultInactivityThreshold = TimeSpan.FromMinutes(30);
    private static readonly IPAddress Ip = IPAddress.Parse("10.8.0.2");

    [Fact]
    public async Task EvaluateAndEnforce_BlocksClient_WhenInactiveLongerThanThreshold()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var services = GetServiceProvider(mock => mock.SetupGet(x => x.UtcNow).Returns(baseTime));
        var manager = services.GetRequiredService<IUserAccessControlManager>();
        var activityTracker = services.GetRequiredService<IClientActivityTracker>();
        activityTracker.RecordActivity(Ip);

        services.SetupMockOf<IDateTimeProvider>(mock => mock
            .SetupGet(x => x.UtcNow)
            .Returns(baseTime + DefaultInactivityThreshold * 1.5));
        services.SetupMockOf<ICommandRunner>(mock => mock
            .Setup(x => x.Execute(It.IsAny<BlockClientRouteCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true));

        await manager.EvaluateAndEnforce();

        services.GetMockOf<ICommandRunner>().Verify(
            x => x.Execute(It.Is<BlockClientRouteCommand>(c => c.Ip.Equals(Ip)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EvaluateAndEnforce_DoesNotReissueBlock_OnSubsequentTicks()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var services = GetServiceProvider(mock => mock.SetupGet(x => x.UtcNow).Returns(baseTime));
        var manager = services.GetRequiredService<IUserAccessControlManager>();
        var activityTracker = services.GetRequiredService<IClientActivityTracker>();
        activityTracker.RecordActivity(Ip);

        services.SetupMockOf<IDateTimeProvider>(mock => mock
            .SetupGet(x => x.UtcNow)
            .Returns(baseTime + DefaultInactivityThreshold * 1.5));
        services.SetupMockOf<ICommandRunner>(mock => mock
            .Setup(x => x.Execute(It.IsAny<BlockClientRouteCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true));

        await manager.EvaluateAndEnforce();
        await manager.EvaluateAndEnforce();

        services.GetMockOf<ICommandRunner>().Verify(
            x => x.Execute(It.IsAny<BlockClientRouteCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EvaluateAndEnforce_UnblocksClient_WhenActivityResumes()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var services = GetServiceProvider(mock => mock.SetupGet(x => x.UtcNow).Returns(baseTime));
        var manager = services.GetRequiredService<IUserAccessControlManager>();
        var activityTracker = services.GetRequiredService<IClientActivityTracker>();
        activityTracker.RecordActivity(Ip);

        services.SetupMockOf<ICommandRunner>(mock => mock
            .Setup(x => x.Execute(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true));
        services.SetupMockOf<IDateTimeProvider>(mock => mock
            .SetupGet(x => x.UtcNow)
            .Returns(baseTime + DefaultInactivityThreshold * 1.5));

        await manager.EvaluateAndEnforce();

        activityTracker.RecordActivity(Ip);
        services.SetupMockOf<IDateTimeProvider>(mock => mock
            .SetupGet(x => x.UtcNow)
            .Returns(baseTime + DefaultInactivityThreshold * 1.5));

        await manager.EvaluateAndEnforce();

        services.GetMockOf<ICommandRunner>().Verify(
            x => x.Execute(It.Is<UnblockClientRouteCommand>(c => c.Ip.Equals(Ip)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EvaluateAndEnforce_RetriesNextTick_WhenCommandFails()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var services = GetServiceProvider(mock => mock.SetupGet(x => x.UtcNow).Returns(baseTime));
        var manager = services.GetRequiredService<IUserAccessControlManager>();
        var activityTracker = services.GetRequiredService<IClientActivityTracker>();
        activityTracker.RecordActivity(Ip);

        services.SetupMockOf<IDateTimeProvider>(mock => mock
            .SetupGet(x => x.UtcNow)
            .Returns(baseTime + DefaultInactivityThreshold * 1.5));
        services.SetupMockOf<ICommandRunner>(mock => mock
            .Setup(x => x.Execute(It.IsAny<BlockClientRouteCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false));

        await manager.EvaluateAndEnforce();
        await manager.EvaluateAndEnforce();

        services.GetMockOf<ICommandRunner>().Verify(
            x => x.Execute(It.IsAny<BlockClientRouteCommand>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RevertAllTaggedRules_RemovesEveryTaggedRuleFromUfwStatus()
    {
        var services = GetServiceProvider();
        var manager = services.GetRequiredService<IUserAccessControlManager>();

        services.SetupMockOf<ICommandRunner>(mock =>
        {
            mock.Setup(x => x.ExecuteAndQuery(It.IsAny<GetUfwStatusCommand>(), It.IsAny<CancellationToken>()))
                .Returns(Lines(
                    "Status: active",
                    "[ 1] Anywhere on wg0        DENY FWD    10.8.0.2            # charon-dns-autoblock",
                    "[ 2] Anywhere on wg0        DENY FWD    10.8.0.3            # charon-dns-autoblock"));
            mock.Setup(x => x.Execute(It.IsAny<UnblockClientRouteCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        });

        await manager.RevertAllTaggedRules();

        services.GetMockOf<ICommandRunner>().Verify(
            x => x.Execute(It.IsAny<UnblockClientRouteCommand>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RevertAllTaggedRules_Throws_WhenUfwInactiveButAccessControlEnabled()
    {
        var services = GetServiceProvider();
        var manager = services.GetRequiredService<IUserAccessControlManager>();

        services.SetupMockOf<ICommandRunner>(mock => mock
            .Setup(x => x.ExecuteAndQuery(It.IsAny<GetUfwStatusCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Lines("Status: inactive")));

        var act = async () => await manager.RevertAllTaggedRules();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RevertAllTaggedRules_DoesNotThrow_WhenUfwInactiveAndAccessControlDisabled()
    {
        var services = GetServiceProvider(accessControlEnabled: false);
        var manager = services.GetRequiredService<IUserAccessControlManager>();

        services.SetupMockOf<ICommandRunner>(mock => mock
            .Setup(x => x.ExecuteAndQuery(It.IsAny<GetUfwStatusCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Lines("Status: inactive")));

        var act = async () => await manager.RevertAllTaggedRules();

        await act.Should().NotThrowAsync();
    }

    private static async IAsyncEnumerable<string> Lines(params string[] lines)
    {
        foreach (var line in lines)
        {
            yield return line;
        }

        await Task.CompletedTask;
    }

    private static IServiceProvider GetServiceProvider(
        Action<Moq.Mock<IDateTimeProvider>> configureClock = null,
        bool accessControlEnabled = true)
    {
        return new ServiceCollection()
            .AddSingleton<IUserAccessControlManager, UserAccessControlManager>()
            .AddSingleton<IClientActivityTracker, ClientActivityTracker>()
            .AddMockOf<ICommandRunner>()
            .AddMockOf(configureClock ?? (mock => mock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow)))
            .AddMockOf<ILogger>()
            .AddSettings(new UserAccessControlSettings
            {
                Enabled = accessControlEnabled,
                InactivityThreshold = DefaultInactivityThreshold,
                ControlledIps = [],
            })
            .BuildServiceProvider();
    }
}
