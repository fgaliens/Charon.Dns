using System.Diagnostics;
using Charon.Dns.Lib.Client;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Tests.Utils.Collections;
using FluentAssertions;
using Xunit.Abstractions;

namespace Charon.Dns.EndToEndTests;

public class EndToEndTests
{
    private const string ClientAddress = "127.0.0.1";
    private readonly ITestOutputHelper _output;

    public EndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public async Task CheckCachedResponse()
    {
        // Arrange
        var client = new DnsClient(ClientAddress);
        
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
        var client = new DnsClient(ClientAddress);
        
        // Act
        var maxResponse = await client.Resolve("max.ru", RecordType.A);

        // Assert
        maxResponse.AnswerRecords.Should().BeEmpty();
    }
    
    [Fact]
    public async Task CheckResolvingForManuallyAddedRecords()
    {
        // Arrange
        var client = new DnsClient(ClientAddress);
        
        // Act
        var response = await client.Resolve("my.home", RecordType.A);

        // Assert
        response.AnswerRecords.Should().NotBeEmpty();
    }
    
    [Fact]
    public async Task CheckUnderLoad()
    {
        // Arrange
        DnsClient[] clients =
        [
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
        ];

        string[] hosts =
        [
             "ya.ru",
             "yandex.cloud",
             "apple.com",
             "google.com",
             "googlevideo.com",
             "selectel.ru",
             "youtube.com",
             "instagram.com",
             "i.ytimg.com",
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
            .Where(x => 
                x.Item1 is not ("medium.com" or "i.ytimg.com") 
                && x.Item2 != RecordType.MX)
            .Select(x => Enumerable.Repeat(x, 3))
            .SelectMany(x => x)
            .ToArray();
        
        Random.Shared.Shuffle(requestsCombination);

        var responses = new List<Task<IResponse>>();
        
        var measure = Stopwatch.StartNew();

        var started = 0;
        var failed = 0;
        
        // Act
        foreach (var (host, recordType) in requestsCombination)
        {
            foreach (var dnsClient in clients)
            {
                responses.Add(Task.Run(async () =>
                {
                    var innerHost = host;
                    var innerRecordType = recordType;
                    try
                    {
                        started++;
                        return await dnsClient.Resolve(innerHost, innerRecordType);
                    }
                    catch
                    {
                        failed++;
                        throw;
                    }
                }));
            }
        }

        try
        {
            await Task.WhenAll(responses);
            
            measure.Stop();
            _output.WriteLine($"Handled {responses.Count} requests in {measure.ElapsedMilliseconds} ms.");
        }
        catch (AggregateException e)
        {
            _output.WriteLine($"Error! Handled {started - failed}/{started} request");
            foreach (var innerException in e.InnerExceptions)
            {
                _output.WriteLine($"[ERR]. {innerException}");
            }
            throw;
        }
        catch (Exception e)
        {
            _output.WriteLine($"Error! Handled {started - failed}/{started} request");
            _output.WriteLine($"[ERR]. {e}");
            throw;
        }
        
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
        DnsClient[] clients =
        [
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
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
        
        var started = 0;
        var failed = 0;
                
        var measure = Stopwatch.StartNew();
        
        // Act
        foreach (var (host, recordType) in requestsCombination)
        {
            foreach (var dnsClient in clients)
            {
                await Task.Delay(2);
                responses.Add(Task.Run(async () =>
                {
                    var innerHost = host;
                    var innerRecordType = recordType;
                    try
                    {
                        started++;
                        return await dnsClient.Resolve(innerHost, innerRecordType);
                    }
                    catch
                    {
                        failed++;
                        throw;
                    }
                }));
            }
        }

        try
        {
            await Task.WhenAll(responses);

            measure.Stop();
            _output.WriteLine($"Handled {responses.Count} requests in {measure.ElapsedMilliseconds} ms.");
        }
        catch (AggregateException e)
        {
            _output.WriteLine($"Error! Handled {started - failed}/{started} request");
            foreach (var innerException in e.InnerExceptions)
            {
                _output.WriteLine($"[ERR]. {innerException}");
            }
            throw;
        }
        catch (Exception e)
        {
            _output.WriteLine($"Error! Handled {started - failed}/{started} request");
            _output.WriteLine($"[ERR]. {e}");
            throw;
        }

        // Assert
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
    
    [Fact]
    public async Task CheckWithManyRequests()
    {
        // Arrange
        DnsClient[] clients =
        [
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
            new DnsClient(ClientAddress),
        ];

        string[] hosts =
        [
             "ya.ru",
             "yandex.cloud",
             "apple.com",
             "google.com",
             "googlevideo.com",
             "selectel.ru",
             "youtube.com",
             "instagram.com",
             "i.ytimg.com",
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
            .Where(x => 
                x.Item1 is not ("medium.com" or "i.ytimg.com") 
                && x.Item2 != RecordType.MX)
            .Select(x => Enumerable.Repeat(x, 30))
            .SelectMany(x => x)
            .ToArray();
        
        Random.Shared.Shuffle(requestsCombination);

        var responses = new List<Task<IResponse>>();
        
        var measure = Stopwatch.StartNew();

        var started = 0;
        var failed = 0;
        
        // Act
        foreach (var (host, recordType) in requestsCombination)
        {
            foreach (var dnsClient in clients)
            {
                await Task.Delay(20);
                responses.Add(Task.Run(async () =>
                {
                    var innerHost = host;
                    var innerRecordType = recordType;
                    try
                    {
                        started++;
                        return await dnsClient.Resolve(innerHost, innerRecordType);
                    }
                    catch
                    {
                        failed++;
                        throw;
                    }
                }));
            }
        }

        try
        {
            await Task.WhenAll(responses);
            
            measure.Stop();
            _output.WriteLine($"Handled {responses.Count} requests in {measure.ElapsedMilliseconds} ms.");
        }
        catch (AggregateException e)
        {
            _output.WriteLine($"Error! Handled {started - failed}/{started} request");
            foreach (var innerException in e.InnerExceptions)
            {
                _output.WriteLine($"[ERR]. {innerException}");
            }
            throw;
        }
        catch (Exception e)
        {
            _output.WriteLine($"Error! Handled {started - failed}/{started} request");
            _output.WriteLine($"[ERR]. {e}");
            throw;
        }
        
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
}