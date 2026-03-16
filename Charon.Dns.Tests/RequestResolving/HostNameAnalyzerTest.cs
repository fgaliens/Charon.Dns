using System;
using Charon.Dns.RequestResolving;
using Charon.Dns.Settings;
using FluentAssertions;
using JetBrains.Annotations;
using Moq;
using Serilog;
using Xunit;

namespace Charon.Dns.Tests.RequestResolving;

[TestSubject(typeof(HostNameAnalyzer))]
public class HostNameAnalyzerTest
{
    [Theory]
    [InlineData("instagram.com")]
    [InlineData("cdninstagram.com")]
    [InlineData("content-fallback.cdninstagram.com")]
    public void ShouldBeSecured_WhenDomainMatchedBySubstring_ReturnsTrue(string hostName)
    {
        // Arrange
        var settings = new RoutingSettings
        {
            RoutingPeriod = TimeSpan.FromHours(1),
            Items = [
            new RoutingSettingsItem
            {
                InterfaceToRouteThrough = "interface-name",
                IpV4RoutingSubnet = 32,
                IpV6RoutingSubnet = 128,
                MatchedByDomainHostNames = [],
                MatchedBySubstringHostNames = [
                    "insta"
                ]
            }
            ],
            BlockedHostNames = [],
        };

        var loggerMock = Mock.Of<ILogger>();
        
        var analyzer = new HostNameAnalyzer(settings, loggerMock);
        
        // Act
        var result = analyzer.ShouldBeSecured(hostName, out var connectionParams);
        
        // Assert
        result.Should().BeTrue();
        connectionParams.Should().NotBeNull();
    }
    
    [Theory]
    [InlineData("instagram.com")]
    [InlineData("cdninstagram.com")]
    [InlineData("content-fallback.cdninstagram.com")]
    public void ShouldBeSecured_WhenDomainNotMatchedBySubstring_ReturnsFalse(string hostName)
    {
        // Arrange
        var settings = new RoutingSettings
        {
            RoutingPeriod = TimeSpan.FromHours(1),
            Items = [
                new RoutingSettingsItem
                {
                    InterfaceToRouteThrough = "interface-name",
                    IpV4RoutingSubnet = 32,
                    IpV6RoutingSubnet = 128,
                    MatchedByDomainHostNames = [],
                    MatchedBySubstringHostNames = [
                        "youtube"
                    ]
                }
            ],
            BlockedHostNames = [],
        };

        var loggerMock = Mock.Of<ILogger>();
        
        var analyzer = new HostNameAnalyzer(settings, loggerMock);
        
        // Act
        var result = analyzer.ShouldBeSecured(hostName, out var connectionParams);
        
        // Assert
        result.Should().BeFalse();
        connectionParams.Should().BeNull();
    }
    
    [Theory]
    [InlineData("instagram.com")]
    [InlineData("example.instagram.com")]
    [InlineData("other-example.content-fallback.instagram.com")]
    public void ShouldBeSecured_WhenDomainMatchedByHostName_ReturnsTrue(string hostName)
    {
        // Arrange
        var settings = new RoutingSettings
        {
            RoutingPeriod = TimeSpan.FromHours(1),
            Items = [
                new RoutingSettingsItem
                {
                    InterfaceToRouteThrough = "interface-name",
                    IpV4RoutingSubnet = 32,
                    IpV6RoutingSubnet = 128,
                    MatchedByDomainHostNames = [
                        "instagram.com",
                    ],
                    MatchedBySubstringHostNames = []
                }
            ],
            BlockedHostNames = [],
        };

        var loggerMock = Mock.Of<ILogger>();
        
        var analyzer = new HostNameAnalyzer(settings, loggerMock);
        
        // Act
        var result = analyzer.ShouldBeSecured(hostName, out var connectionParams);
        
        // Assert
        result.Should().BeTrue();
        connectionParams.Should().NotBeNull();
    }
    
    [Theory]
    [InlineData("instagra2.com")]
    [InlineData("example.instagra2.com")]
    [InlineData("other-example.content-fallback.instagra2.com")]
    public void ShouldBeSecured_WhenDomainNotMatchedByHostName_ReturnsFalse(string hostName)
    {
        // Arrange
        var settings = new RoutingSettings
        {
            RoutingPeriod = TimeSpan.FromHours(1),
            Items = [
                new RoutingSettingsItem
                {
                    InterfaceToRouteThrough = "interface-name",
                    IpV4RoutingSubnet = 32,
                    IpV6RoutingSubnet = 128,
                    MatchedByDomainHostNames = [
                        "instagram.com",
                    ],
                    MatchedBySubstringHostNames = []
                }
            ],
            BlockedHostNames = [],
        };

        var loggerMock = Mock.Of<ILogger>();
        
        var analyzer = new HostNameAnalyzer(settings, loggerMock);
        
        // Act
        var result = analyzer.ShouldBeSecured(hostName, out var connectionParams);
        
        // Assert
        result.Should().BeFalse();
        connectionParams.Should().BeNull();
    }
}
