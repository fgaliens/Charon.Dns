using System;
using System.Linq;
using System.Net;
using Charon.Dns.Cache;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Settings;
using Charon.Dns.Utils;
using FluentAssertions;
using JetBrains.Annotations;
using Moq;
using Serilog;
using Xunit;

namespace Charon.Dns.Tests.Cache;

[TestSubject(typeof(DnsCache))]
public class DnsCacheTest
{
    private const uint DnssecOkFlag = 0x8000;
    private static readonly Domain Name = new("ya.ru");
    private static readonly Domain RootName = new(Array.Empty<string>());

    private readonly DnsCache _dnsCache;

    public DnsCacheTest()
    {
        var dateTimeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == DateTimeOffset.UnixEpoch);
        var cacheSettings = new CacheSettings
        {
            Enabled = true,
            TimeToLive = TimeSpan.FromMinutes(5),
        };
        var logger = Mock.Of<ILogger>();
        _dnsCache = new DnsCache(dateTimeProvider, cacheSettings, logger);
    }

    [Fact]
    public void TryGetResponse_WhenRequestsDifferOnlyByCookie_ReturnsCachedResponse()
    {
        // Arrange
        var firstRequest = CreateRequestWithCookie(1);
        var secondRequest = CreateRequestWithCookie(2);
        var response = CreateResponse(firstRequest);
        _dnsCache.AddResponse(firstRequest, response);

        // Act
        var found = _dnsCache.TryGetResponse(secondRequest, out var cachedResponse);

        // Assert
        found.Should().BeTrue();
        cachedResponse!.Id.Should().Be(secondRequest.Id);
        cachedResponse.AnswerRecords.Should().ContainSingle();
    }

    [Fact]
    public void TryGetResponse_ReturnsResponseWithoutCookieOfAnotherClient()
    {
        // Arrange
        var paddingOption = CreateOption(12, [0, 0]);
        var firstCookieOption = CreateCookieOption(1);
        var firstRequest = CreateRequestWithCookie(1);
        var secondRequest = CreateRequestWithCookie(2);
        var responseOpt = CreateOptRecord(0, [..firstCookieOption, ..paddingOption]);
        var response = CreateResponse(firstRequest, responseOpt);
        _dnsCache.AddResponse(firstRequest, response);

        // Act
        _dnsCache.TryGetResponse(secondRequest, out var cachedResponse);

        // Assert
        var cachedOpt = cachedResponse!.AdditionalRecords.Should().ContainSingle().Subject;
        cachedOpt.Type.Should().Be(RecordType.OPT);
        cachedOpt.Data.Should().Equal(paddingOption);
        response.AdditionalRecords.Single().Data.Should().Equal(responseOpt.Data,
            "the response returned to the first client must keep its cookie");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TryGetResponse_WhenEdnsPresenceDiffers_ReturnsNothing(bool firstHasEdns, bool secondHasEdns)
    {
        // Arrange
        var firstRequest = CreateRequestWithEdns(firstHasEdns, 0);
        var secondRequest = CreateRequestWithEdns(secondHasEdns, 0);
        var response = CreateResponse(firstRequest);
        _dnsCache.AddResponse(firstRequest, response);

        // Act
        var found = _dnsCache.TryGetResponse(secondRequest, out _);

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public void TryGetResponse_WhenDnssecOkFlagDiffers_ReturnsNothing()
    {
        // Arrange
        var firstRequest = CreateRequestWithEdns(true, 0);
        var secondRequest = CreateRequestWithEdns(true, DnssecOkFlag);
        var response = CreateResponse(firstRequest);
        _dnsCache.AddResponse(firstRequest, response);

        // Act
        var found = _dnsCache.TryGetResponse(secondRequest, out _);

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public void TryGetResponse_WhenRecursionDesiredDiffers_ReturnsNothing()
    {
        // Arrange
        var firstRequest = CreateRequest();
        var secondRequest = CreateRequest();
        secondRequest.RecursionDesired = false;
        var response = CreateResponse(firstRequest);
        _dnsCache.AddResponse(firstRequest, response);

        // Act
        var found = _dnsCache.TryGetResponse(secondRequest, out _);

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public void AddResponse_WhenResponseIsTruncated_DoesNotCacheIt()
    {
        // Arrange
        var request = CreateRequest();
        // A truncated response may carry a part of the answer, the client retries over TCP for the rest
        var response = CreateResponse(request);
        response.Truncated = true;

        // Act
        _dnsCache.AddResponse(request, response);

        // Assert
        var found = _dnsCache.TryGetResponse(request, out _);
        found.Should().BeFalse();
    }

    [Fact]
    public void TryGetResponse_KeepsRecursionFlags()
    {
        // Arrange
        var request = CreateRequest();
        var response = CreateResponse(request);
        _dnsCache.AddResponse(request, response);

        // Act
        _dnsCache.TryGetResponse(request, out var cachedResponse);

        // Assert
        cachedResponse!.RecursionDesired.Should().BeTrue();
        cachedResponse.RecursionAvailable.Should().BeTrue();
    }

    private static Request CreateRequestWithCookie(byte clientCookieSeed)
    {
        var cookieOption = CreateCookieOption(clientCookieSeed);
        var optRecord = CreateOptRecord(0, cookieOption);
        return CreateRequest(optRecord);
    }

    private static Request CreateRequestWithEdns(bool hasEdns, uint flags)
    {
        if (!hasEdns)
        {
            return CreateRequest();
        }

        var optRecord = CreateOptRecord(flags, []);
        return CreateRequest(optRecord);
    }

    private static Request CreateRequest(params IResourceRecord[] additionalRecords)
    {
        var request = new Request
        {
            RecursionDesired = true,
        };
        var question = new Question(Name, RecordType.A);
        request.Questions.Add(question);

        foreach (var additionalRecord in additionalRecords)
        {
            request.AdditionalRecords.Add(additionalRecord);
        }

        return request;
    }

    private static Response CreateResponse(IRequest request, params IResourceRecord[] additionalRecords)
    {
        var response = Response.FromRequest(request);
        response.RecursionAvailable = true;

        var address = IPAddress.Parse("77.88.55.242");
        var answer = new IpAddressResourceRecord(Name, address, TimeSpan.FromMinutes(1));
        response.AnswerRecords.Add(answer);

        foreach (var additionalRecord in additionalRecords)
        {
            response.AdditionalRecords.Add(additionalRecord);
        }

        return response;
    }

    private static ResourceRecord CreateOptRecord(uint flags, byte[] options)
    {
        var flagsAsTtl = TimeSpan.FromSeconds(flags);
        return new ResourceRecord(RootName, options, RecordType.OPT, (RecordClass)1232, flagsAsTtl);
    }

    private static byte[] CreateCookieOption(byte clientCookieSeed)
    {
        var clientCookie = Enumerable.Repeat(clientCookieSeed, 8).ToArray();
        return CreateOption(10, clientCookie);
    }

    private static byte[] CreateOption(ushort code, byte[] data)
    {
        return [(byte)(code >> 8), (byte)code, (byte)(data.Length >> 8), (byte)data.Length, ..data];
    }
}
