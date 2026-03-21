using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using Charon.Dns.Cache;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.RequestResolving;
using Charon.Dns.Tests.Utils.Mock;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Serilog;
using Xunit;

namespace Charon.Dns.Tests.RequestResolving;

[TestSubject(typeof(SmartRequestResolver))]
public class SmartRequestResolverTest
{
    private readonly IServiceProvider _serviceProvider = new ServiceCollection()
        .AddSingleton<SmartRequestResolver>()
        .AddMockOf<IDefaultRequestResolver>()
        .AddMockOf<ISafeRequestResolver>()
        .AddMockOf<IHostNameAnalyzer>()
        .AddMockOf<IDnsCache>()
        .AddMockOf<ILogger>()
        .BuildServiceProvider();
    private readonly Fixture _fixture = new();

    public SmartRequestResolverTest()
    {
        _fixture.Register(() => new Domain(_fixture.Create<string>()));
    }
    
    [Fact]
    public async Task Resolve_WhenHostNameIsNotSecuredOrBlocked()
    {
        // Arrange
        var requestQuestions = _fixture
            .CreateMany<Question>(1)
            .ToList();
        var request = Mock.Of<IRequest>(x => x.Questions == requestQuestions);
        var endPoint = _fixture.Create<IPEndPoint>();
        var expectedResponse = Mock.Of<IResponse>();
        
        _serviceProvider.SetupMockOf<IHostNameAnalyzer>(mock =>
        {
            mock.Setup(x => x
                .ShouldBeSecured(It.Is<string>(y => y == requestQuestions[0].Name.ToString())))
                .Returns(false);
            mock.Setup(x => x
                .ShouldBeBlocked(It.Is<string>(y => y == requestQuestions[0].Name.ToString())))
                .Returns(false);
        })
        .SetupMockOf<IDefaultRequestResolver>(mock =>
        {
            mock.Setup(x => x.Resolve(
                It.Is<IRequest>(y => y == request), 
                It.Is<IPEndPoint>(y => y == endPoint), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResponse);
        });
        
        var requestResolver = _serviceProvider.GetRequiredService<SmartRequestResolver>();
        
        // Act
        var actualResponse = await requestResolver.Resolve(request, endPoint, CancellationToken.None);
        
        // Assert
        actualResponse.Should().Be(expectedResponse);
        
        var dnsCacheMock = _serviceProvider.GetMockOf<IDnsCache>();
        IResponse cachedResponse;
        dnsCacheMock.Verify(x => x.TryGetResponse(request, out cachedResponse), Times.Once);
        dnsCacheMock.Verify(x => x.AddResponse(request, expectedResponse), Times.Once);
    }
    
    [Fact]
    public async Task Resolve_WhenHostNameIsSecured()
    {
        // Arrange
        var requestQuestions = _fixture
            .CreateMany<Question>(1)
            .ToList();
        var request = Mock.Of<IRequest>(x => x.Questions == requestQuestions);
        var endPoint = _fixture.Create<IPEndPoint>();
        var expectedResponse = Mock.Of<IResponse>();
        
        _serviceProvider.SetupMockOf<IHostNameAnalyzer>(mock =>
            {
                mock.Setup(x => x
                        .ShouldBeSecured(It.Is<string>(y => y == requestQuestions[0].Name.ToString())))
                    .Returns(true);
                mock.Setup(x => x
                        .ShouldBeBlocked(It.Is<string>(y => y == requestQuestions[0].Name.ToString())))
                    .Returns(false);
            })
            .SetupMockOf<ISafeRequestResolver>(mock =>
            {
                mock.Setup(x => x.Resolve(
                        It.Is<IRequest>(y => y == request), 
                        It.Is<IPEndPoint>(y => y == endPoint), 
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedResponse);
            });
        
        var requestResolver = _serviceProvider.GetRequiredService<SmartRequestResolver>();
        
        // Act
        var actualResponse = await requestResolver.Resolve(request, endPoint, CancellationToken.None);
        
        // Assert
        actualResponse.Should().Be(expectedResponse);
        
        var dnsCacheMock = _serviceProvider.GetMockOf<IDnsCache>();
        IResponse cachedResponse;
        dnsCacheMock.Verify(x => x.TryGetResponse(request, out cachedResponse), Times.Once);
        dnsCacheMock.Verify(x => x.AddResponse(request, expectedResponse), Times.Once);
    }
    
    [Fact]
    public async Task Resolve_WhenHostNameIsBlocked()
    {
        // Arrange
        var requestQuestions = _fixture
            .CreateMany<Question>(1)
            .ToList();
        var request = Mock.Of<IRequest>(x => x.Questions == requestQuestions);
        var endPoint = _fixture.Create<IPEndPoint>();
        
        _serviceProvider.SetupMockOf<IHostNameAnalyzer>(mock =>
        {
            mock.Setup(x => x
                    .ShouldBeSecured(It.Is<string>(y => y == requestQuestions[0].Name.ToString())))
                .Returns(false);
            mock.Setup(x => x
                    .ShouldBeBlocked(It.Is<string>(y => y == requestQuestions[0].Name.ToString())))
                .Returns(true);
        });
        
        var requestResolver = _serviceProvider.GetRequiredService<SmartRequestResolver>();
        
        // Act
        var actualResponse = await requestResolver.Resolve(request, endPoint, CancellationToken.None);
        
        // Assert
        actualResponse.Questions.Should().BeEquivalentTo(requestQuestions);
        actualResponse.AnswerRecords.Should().BeEmpty();
        
        var defaultRequestResolver = _serviceProvider.GetMockOf<IDefaultRequestResolver>();
        defaultRequestResolver.Verify(x => x.Resolve(
            It.IsAny<IRequest>(),
            It.IsAny<IPEndPoint>(),
            It.IsAny<CancellationToken>()), Times.Never);
        
        var safeRequestResolver = _serviceProvider.GetMockOf<ISafeRequestResolver>();
        safeRequestResolver.Verify(x => x.Resolve(
            It.IsAny<IRequest>(),
            It.IsAny<IPEndPoint>(),
            It.IsAny<CancellationToken>()), Times.Never);
        
        var dnsCacheMock = _serviceProvider.GetMockOf<IDnsCache>();
        IResponse cachedResponse;
        dnsCacheMock.Verify(x => x.TryGetResponse(request, out cachedResponse), Times.Once);
        dnsCacheMock.Verify(x => x.AddResponse(request, It.IsAny<IResponse>()), Times.Once);
    }
    
    [Fact]
    public async Task Resolve_WhenHostNameIsSecuredAndBlocked_ShouldBeBlocked()
    {
        // Arrange
        var requestQuestions = _fixture
            .CreateMany<Question>(1)
            .ToList();
        var request = Mock.Of<IRequest>(x => x.Questions == requestQuestions);
        var endPoint = _fixture.Create<IPEndPoint>();
        
        _serviceProvider.SetupMockOf<IHostNameAnalyzer>(mock =>
        {
            mock.Setup(x => x
                    .ShouldBeSecured(It.Is<string>(y => y == requestQuestions[0].Name.ToString())))
                .Returns(true);
            mock.Setup(x => x
                    .ShouldBeBlocked(It.Is<string>(y => y == requestQuestions[0].Name.ToString())))
                .Returns(true);
        });
        
        var requestResolver = _serviceProvider.GetRequiredService<SmartRequestResolver>();
        
        // Act
        var actualResponse = await requestResolver.Resolve(request, endPoint, CancellationToken.None);
        
        // Assert
        actualResponse.Questions.Should().BeEquivalentTo(requestQuestions);
        actualResponse.AnswerRecords.Should().BeEmpty();
        
        var defaultRequestResolver = _serviceProvider.GetMockOf<IDefaultRequestResolver>();
        defaultRequestResolver.Verify(x => x.Resolve(
            It.IsAny<IRequest>(),
            It.IsAny<IPEndPoint>(),
            It.IsAny<CancellationToken>()), Times.Never);
        
        var safeRequestResolver = _serviceProvider.GetMockOf<ISafeRequestResolver>();
        safeRequestResolver.Verify(x => x.Resolve(
            It.IsAny<IRequest>(),
            It.IsAny<IPEndPoint>(),
            It.IsAny<CancellationToken>()), Times.Never);
        
        var dnsCacheMock = _serviceProvider.GetMockOf<IDnsCache>();
        IResponse cachedResponse;
        dnsCacheMock.Verify(x => x.TryGetResponse(request, out cachedResponse), Times.Once);
        dnsCacheMock.Verify(x => x.AddResponse(request, It.IsAny<IResponse>()), Times.Once);
    }
}
