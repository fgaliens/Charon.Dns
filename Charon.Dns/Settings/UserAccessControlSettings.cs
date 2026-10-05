using System.Net;
using Charon.Dns.Extensions;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public class UserAccessControlSettings : ISettings<UserAccessControlSettings>
{
    public required bool Enabled { get; init; }

    public required TimeSpan InactivityThreshold { get; init; }

    public required IReadOnlyCollection<IPNetwork> ControlledIps { get; init; }

    public static UserAccessControlSettings Initialize(IConfiguration config)
    {
        var accessControlSection = config.GetSection("UserAccessControl");
        var enabled = accessControlSection.GetSectionValue("Enabled", false);
        var inactivityThreshold = accessControlSection.GetSectionValue("InactivityThreshold", TimeSpan.FromHours(1));
        var controlledIps = accessControlSection
            .GetSection("ControlledIps")
            .GetChildren()
            .Select(x => x.GetSectionValue<IPNetwork>())
            .ToArray();

        return new()
        {
            Enabled = enabled,
            InactivityThreshold = inactivityThreshold,
            ControlledIps = controlledIps,
        };
    }
}