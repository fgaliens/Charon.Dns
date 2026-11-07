#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Server;
using Charon.Dns.Lib.Server.Zones;
using Charon.Dns.Lib.Tracing;
using Xunit;

namespace Charon.Dns.Lib.Tests.Server.Zones
{
    public class ZoneResolverTest
    {
        private static readonly Domain Origin = new("my.home");
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

        [Theory]
        [InlineData(RecordType.A, "my.home")]
        [InlineData(RecordType.A, "MY.home")]
        [InlineData(RecordType.ANY, "my.home")]
        [InlineData(RecordType.ANY, "My.Home")]
        public async Task ResolvesStandaloneRecord(RecordType recordType, string domain)
        {
            var record = CreateAddressRecord(Origin, "192.168.0.1");
            var resolver = new ZoneResolverBuilder()
                .AddStandaloneRecord(record)
                .Build();

            var response = await Resolve(resolver, domain, recordType);

            Assert.False(response.AuthorativeServer);
            var answer = Assert.Single(response.AnswerRecords);
            Assert.Equal(RecordType.A, answer.Type);
            Assert.Equal("my.home", answer.Name.ToString());
            Assert.Equal("192.168.0.1", ((IpAddressResourceRecord)answer).IpAddress.ToString());
        }

        [Fact]
        public async Task StandaloneMiss_ReturnsNoAnswers()
        {
            var record = CreateAddressRecord(Origin, "192.168.0.1");
            var resolver = new ZoneResolverBuilder()
                .AddStandaloneRecord(record)
                .Build();

            var response = await Resolve(resolver, "my.home", RecordType.AAAA);

            Assert.False(response.AuthorativeServer);
            Assert.Empty(response.AnswerRecords);
            Assert.Empty(response.AuthorityRecords);
        }

        [Theory]
        [InlineData(RecordType.A, "10.0.0.1")]
        [InlineData(RecordType.AAAA, "fd00::1")]
        public async Task ResolvesZoneRecord_Authoritatively(RecordType recordType, string address)
        {
            var ipV4Record = CreateAddressRecord(Origin, "10.0.0.1");
            var ipV6Record = CreateAddressRecord(Origin, "fd00::1");
            var resolver = CreateZone(builder => builder
                .AddRecord(Origin, ipV4Record)
                .AddRecord(Origin, ipV6Record));

            var response = await Resolve(resolver, "my.home", recordType);

            Assert.True(response.AuthorativeServer);
            Assert.Equal(ResponseCode.NoError, response.ResponseCode);
            var answer = Assert.Single(response.AnswerRecords);
            Assert.Equal(address, ((IpAddressResourceRecord)answer).IpAddress.ToString());
        }

        [Fact]
        public async Task MissingName_ReturnsNameErrorWithSoa()
        {
            var record = CreateAddressRecord(Origin, "10.0.0.1");
            var resolver = CreateZone(builder => builder.AddRecord(Origin, record));

            var response = await Resolve(resolver, "nope.my.home", RecordType.A);

            Assert.True(response.AuthorativeServer);
            Assert.Equal(ResponseCode.NameError, response.ResponseCode);
            Assert.Empty(response.AnswerRecords);
            var authority = Assert.Single(response.AuthorityRecords);
            Assert.Equal(RecordType.SOA, authority.Type);
        }

        [Fact]
        public async Task MissingType_ReturnsNoDataWithSoa()
        {
            var record = CreateAddressRecord(Origin, "10.0.0.1");
            var resolver = CreateZone(builder => builder.AddRecord(Origin, record));

            var response = await Resolve(resolver, "my.home", RecordType.MX);

            Assert.True(response.AuthorativeServer);
            Assert.Equal(ResponseCode.NoError, response.ResponseCode);
            Assert.Empty(response.AnswerRecords);
            var authority = Assert.Single(response.AuthorityRecords);
            Assert.Equal(RecordType.SOA, authority.Type);
        }

        [Fact]
        public async Task SoaQuery_ReturnsSoa()
        {
            var resolver = CreateZone(_ => { });

            var response = await Resolve(resolver, "my.home", RecordType.SOA);

            var answer = Assert.Single(response.AnswerRecords);
            Assert.Equal(RecordType.SOA, answer.Type);
        }

        [Fact]
        public async Task FollowsCanonicalNameInsideZone()
        {
            var nas = new Domain("nas.my.home");
            var addressRecord = CreateAddressRecord(Origin, "10.0.0.1");
            var canonicalNameRecord = new CanonicalNameResourceRecord(nas, Origin, Ttl);
            var resolver = CreateZone(builder => builder
                .AddRecord(Origin, addressRecord)
                .AddRecord(Origin, canonicalNameRecord));

            var response = await Resolve(resolver, "nas.my.home", RecordType.A);

            Assert.True(response.AuthorativeServer);
            var answerTypes = response.AnswerRecords.Select(record => record.Type).ToArray();
            Assert.Equal([RecordType.CNAME, RecordType.A], answerTypes);
        }

        [Fact]
        public async Task CanonicalNameQuery_DoesNotFollow()
        {
            var nas = new Domain("nas.my.home");
            var addressRecord = CreateAddressRecord(Origin, "10.0.0.1");
            var canonicalNameRecord = new CanonicalNameResourceRecord(nas, Origin, Ttl);
            var resolver = CreateZone(builder => builder
                .AddRecord(Origin, addressRecord)
                .AddRecord(Origin, canonicalNameRecord));

            var response = await Resolve(resolver, "nas.my.home", RecordType.CNAME);

            var answer = Assert.Single(response.AnswerRecords);
            Assert.Equal(RecordType.CNAME, answer.Type);
        }

        [Fact]
        public async Task CanonicalNameOutsideZone_ReturnsOnlyCanonicalName()
        {
            var nas = new Domain("nas.my.home");
            var target = new Domain("example.com");
            var canonicalNameRecord = new CanonicalNameResourceRecord(nas, target, Ttl);
            var resolver = CreateZone(builder => builder.AddRecord(Origin, canonicalNameRecord));

            var response = await Resolve(resolver, "nas.my.home", RecordType.A);

            Assert.Equal(ResponseCode.NoError, response.ResponseCode);
            var answer = Assert.Single(response.AnswerRecords);
            Assert.Equal(RecordType.CNAME, answer.Type);
        }

        [Fact]
        public async Task CanonicalNameLoop_Terminates()
        {
            var a = new Domain("a.my.home");
            var b = new Domain("b.my.home");
            var aRecord = new CanonicalNameResourceRecord(a, b, Ttl);
            var bRecord = new CanonicalNameResourceRecord(b, a, Ttl);
            var resolver = CreateZone(builder => builder
                .AddRecord(Origin, aRecord)
                .AddRecord(Origin, bRecord));

            var response = await Resolve(resolver, "a.my.home", RecordType.A);

            Assert.All(response.AnswerRecords, record => Assert.Equal(RecordType.CNAME, record.Type));
        }

        [Theory]
        [InlineData("127.0.0.1", "192.168.1.1")]
        [InlineData("10.0.0.5", "10.0.0.1")]
        public async Task FiltersRecordsByRemoteAddress(string remoteAddress, string expectedAddress)
        {
            var commonRecord = CreateAddressRecord(Origin, "10.0.0.1");
            var restrictedRecord = CreateAddressRecord(Origin, "192.168.1.1");
            var restrictedTo = IPAddress.Parse("127.0.0.1");
            var resolver = CreateZone(builder => builder
                .AddRecord(Origin, commonRecord)
                .AddRecord(Origin, restrictedRecord, restrictedTo));

            var parsedRemoteAddress = IPAddress.Parse(remoteAddress);
            var response = await Resolve(resolver, "my.home", RecordType.A, parsedRemoteAddress);

            var answer = Assert.Single(response.AnswerRecords);
            Assert.Equal(expectedAddress, ((IpAddressResourceRecord)answer).IpAddress.ToString());
        }

        [Fact]
        public async Task ResolvesFromDeepestZone()
        {
            var subOrigin = new Domain("sub.my.home");
            var host = new Domain("host.sub.my.home");
            var soa = CreateSoa(Origin);
            var subSoa = CreateSoa(subOrigin);
            var record = CreateAddressRecord(host, "10.0.0.7");
            var resolver = new ZoneResolverBuilder()
                .AddZone(soa)
                .AddZone(subSoa)
                .AddRecord(subOrigin, record)
                .Build();

            var response = await Resolve(resolver, "host.sub.my.home", RecordType.A);

            Assert.Single(response.AnswerRecords);
        }

        [Fact]
        public void RecordOutsideOfZone_Throws()
        {
            var soa = CreateSoa(Origin);
            var builder = new ZoneResolverBuilder().AddZone(soa);
            var outsideName = new Domain("example.com");
            var record = CreateAddressRecord(outsideName, "10.0.0.1");

            Assert.Throws<ArgumentException>(() => builder.AddRecord(Origin, record));
        }

        [Fact]
        public void CanonicalNameWithOtherRecords_Throws()
        {
            var nas = new Domain("nas.my.home");
            var soa = CreateSoa(Origin);
            var canonicalNameRecord = new CanonicalNameResourceRecord(nas, Origin, Ttl);
            var addressRecord = CreateAddressRecord(nas, "10.0.0.1");
            var builder = new ZoneResolverBuilder()
                .AddZone(soa)
                .AddRecord(Origin, canonicalNameRecord)
                .AddRecord(Origin, addressRecord);

            Assert.Throws<ArgumentException>(() => builder.Build());
        }

        [Fact]
        public async Task FallbackResolver_DoesNotForwardAuthoritativeResponse()
        {
            var zoneResolver = CreateZone(_ => { });
            var nextResolver = new CountingRequestResolver();
            var fallbackResolver = new DnsServer.FallbackRequestResolver(zoneResolver, nextResolver);

            var response = await Resolve(fallbackResolver, "nope.my.home", RecordType.A);
            await Resolve(fallbackResolver, "example.com", RecordType.A);

            Assert.Equal(ResponseCode.NameError, response.ResponseCode);
            Assert.Equal(1, nextResolver.Calls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RecursionAvailable_DependsOnBuilderOption(bool recursionAvailable)
        {
            var soa = CreateSoa(Origin);
            var builder = new ZoneResolverBuilder().AddZone(soa);
            if (recursionAvailable)
            {
                builder.WithRecursionAvailable();
            }
            var resolver = builder.Build();

            var response = await Resolve(resolver, "my.home", RecordType.SOA);

            Assert.Equal(recursionAvailable, response.RecursionAvailable);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RecursionDesired_IsCopiedFromRequest(bool recursionDesired)
        {
            var resolver = CreateZone(_ => { });
            var question = new Question(Origin, RecordType.SOA);
            IRequest request = new Request();
            request.RecursionDesired = recursionDesired;
            request.Questions.Add(question);

            var response = await resolver.Resolve(request, RequestTrace.Empty);

            Assert.Equal(recursionDesired, response.RecursionDesired);
        }

        private static ZoneResolver CreateZone(Action<ZoneResolverBuilder> configure)
        {
            var soa = CreateSoa(Origin);
            var builder = new ZoneResolverBuilder().AddZone(soa);
            configure(builder);
            return builder.Build();
        }

        private static IpAddressResourceRecord CreateAddressRecord(Domain name, string address)
        {
            var ipAddress = IPAddress.Parse(address);
            return new IpAddressResourceRecord(name, ipAddress, Ttl);
        }

        private static StartOfAuthorityResourceRecord CreateSoa(Domain origin)
        {
            var primaryNameServer = new Domain($"ns.{origin}");
            var responsibleMailbox = new Domain($"hostmaster.{origin}");
            return new StartOfAuthorityResourceRecord(origin, primaryNameServer, responsibleMailbox, ttl: Ttl);
        }

        private static Task<IResponse> Resolve(
            IRequestResolver resolver,
            string domain,
            RecordType recordType,
            IPAddress? remoteAddress = null)
        {
            var name = new Domain(domain);
            var question = new Question(name, recordType);

            IRequest request = new Request();
            request.Id = 1;
            request.OperationCode = OperationCode.Query;
            request.Questions.Add(question);

            var trace = remoteAddress is null
                ? RequestTrace.Empty
                : RequestTrace.Empty with { RemoteEndPoint = new IPEndPoint(remoteAddress, 53) };

            return resolver.Resolve(request, trace);
        }

        private class CountingRequestResolver : IRequestResolver
        {
            public int Calls { get; private set; }

            public Task<IResponse> Resolve(
                IRequest request,
                RequestTrace trace,
                CancellationToken cancellationToken = default)
            {
                Calls++;
                var response = Response.FromRequest(request);
                return Task.FromResult<IResponse>(response);
            }
        }
    }
}
