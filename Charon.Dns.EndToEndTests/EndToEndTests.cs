using System.Diagnostics;
using Charon.Dns.Lib.Client;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Tests.Utils.Collections;
using FluentAssertions;
using Xunit.Abstractions;

namespace Charon.Dns.EndToEndTests;

public class EndToEndTests : IDisposable
{
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(5);
    
    private readonly ITestOutputHelper _output;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public EndToEndTests(ITestOutputHelper output)
    {
        _output = output;
        _cancellationTokenSource = new CancellationTokenSource();
        _ = Program.Main([], _cancellationTokenSource.Token);
    }
    
    [Fact]
    public async Task CheckCachedResponse()
    {
        // Arrange
        await Task.Delay(InitializationTimeout);
        
        var client = new DnsClient("127.0.0.1");
        
        // Act
        var yandexIpsResponseTask1 = client.Resolve("ya.ru", RecordType.A);
        var yandexIpsResponseTask2 = client.Resolve("ya.ru", RecordType.A);

        await Task.WhenAll(yandexIpsResponseTask1, yandexIpsResponseTask2);
        
        var yandexIpsResponse1 = await yandexIpsResponseTask1;
        var yandexIpsResponse2 = await yandexIpsResponseTask2;

        // Assert
        yandexIpsResponse1.AnswerRecords.Should().NotBeEmpty();
        yandexIpsResponse2.AnswerRecords.Should().NotBeEmpty();

        yandexIpsResponse1.AnswerRecords.Should().AllSatisfy(answer =>
        {
            yandexIpsResponse2
                .AnswerRecords
                .Should()
                .Contain(x => x.Data.SequenceEqual(answer.Data));
        });
    }
    
    [Fact]
    public async Task CheckBlockedResponseReturnsNothing()
    {
        // Arrange
        await Task.Delay(InitializationTimeout);
        
        var client = new DnsClient("127.0.0.1");
        
        // Act
        var maxResponse = await client.Resolve("max.ru", RecordType.A);

        // Assert
        maxResponse.AnswerRecords.Should().BeEmpty();
    }
    
    [Fact]
    public async Task CheckUnderLoad()
    {
        // Arrange
        await Task.Delay(InitializationTimeout);

        DnsClient[] clients =
        [
            new DnsClient("127.0.0.1"),
            new DnsClient("127.0.0.1"),
            new DnsClient("127.0.0.1"),
            new DnsClient("127.0.0.1"),
            new DnsClient("127.0.0.1"),
            new DnsClient("127.0.0.1"),
        ];

        string[] hosts =
        [
            "ya.ru",
            "apple.com",
            "google.com",
            "selectel.ru",
            "youtube.com",
            "amdm.ru",
            "medium.com",
            "max.ru",
        ];

        RecordType[] recordTypes =
        [
            RecordType.A,
            RecordType.AAAA,
            RecordType.MX,
        ];

        var requestsCombination = hosts
            .CombineWith(recordTypes)
            .Select(x => Enumerable.Repeat(x, 3))
            .SelectMany(x => x)
            .ToArray();
        
        Random.Shared.Shuffle(requestsCombination);

        var responses = new List<Task<IResponse>>();
        
        var measure = Stopwatch.StartNew();
        
        // Act
        foreach (var (host, recordType) in requestsCombination)
        {
            foreach (var dnsClient in clients)
            {
                responses.Add(dnsClient.Resolve(host, recordType));
            }
        }
        
        await Task.WhenAll(responses);
        
        measure.Stop();

        // Assert
        _output.WriteLine($"Handled {responses.Count} requests in {measure.ElapsedMilliseconds} ms.");
        
        foreach (var responseTask in responses)
        {
            var response = await responseTask;
            var isBlocked = response.Questions.Any(x => x.Name.ToString().Equals("max.ru", StringComparison.OrdinalIgnoreCase));
            var isMxQuestion = response.Questions.Any(x => x.Type is  RecordType.MX);
            if (isBlocked)
            {
                response.AnswerRecords.Should().BeEmpty();
            }
            else
            {
                if (isMxQuestion && response.AnswerRecords.Count == 0)
                {
                    continue;
                }
                
                response.AnswerRecords.Should().NotBeEmpty($"Expected that response {response} is not empty");
                response.Questions.Should().HaveCount(1);

                response.AnswerRecords.Should().AllSatisfy(answer =>
                {
                    answer.Type.Should().Be(response.Questions[0].Type);
                    answer.Class.Should().Be(response.Questions[0].Class);
                });
            }
        }
    }
    
    [Fact]
    public async Task CheckUnderLoadWithSameRequests()
    {
        // Arrange
        await Task.Delay(InitializationTimeout);

        DnsClient[] clients =
        [
            new DnsClient("127.0.0.1"),
            new DnsClient("127.0.0.1"),
            new DnsClient("127.0.0.1"),
            new DnsClient("127.0.0.1"),
            new DnsClient("127.0.0.1"),
            new DnsClient("127.0.0.1"),
        ];

        string[] hosts =
        [
            "ya.ru",
            "google.com",
        ];

        RecordType[] recordTypes =
        [
            RecordType.A,
            RecordType.AAAA,
        ];

        var requestsCombination = hosts
            .CombineWith(recordTypes)
            .Select(x => Enumerable.Repeat(x, 100))
            .SelectMany(x => x)
            .ToArray();
        
        Random.Shared.Shuffle(requestsCombination);

        var responses = new List<Task<IResponse>>();
        
        var measure = Stopwatch.StartNew();
        
        // Act
        foreach (var (host, recordType) in requestsCombination)
        {
            foreach (var dnsClient in clients)
            {
                responses.Add(dnsClient.Resolve(host, recordType));
            }
        }
        
        await Task.WhenAll(responses);
        
        measure.Stop();

        // Assert
        _output.WriteLine($"Handled {responses.Count} requests in {measure.ElapsedMilliseconds} ms.");
        
        foreach (var responseTask in responses)
        {
            var response = await responseTask;
            
            response.AnswerRecords.Should().NotBeEmpty($"Expected that response {response} is not empty");
            response.Questions.Should().HaveCount(1);

            response.AnswerRecords.Should().AllSatisfy(answer =>
            {
                answer.Type.Should().Be(response.Questions[0].Type);
                answer.Class.Should().Be(response.Questions[0].Class);
            });
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
    }
}
