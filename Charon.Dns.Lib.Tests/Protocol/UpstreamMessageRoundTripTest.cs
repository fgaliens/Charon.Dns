#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.EqualityComparers;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Xunit;

namespace Charon.Dns.Lib.Tests.Protocol
{
    /// <summary>
    /// Real messages captured from public resolvers (1.1.1.1, 77.88.8.8).
    /// Upstreams may compress names differently,
    /// so the content is compared after the second parsing instead of the bytes.
    /// </summary>
    public class UpstreamMessageRoundTripTest
    {
        private const string FixtureDirectory = "Upstream";
        // DNSSEC types are not a part of RecordType, such records are kept as raw data
        private const RecordType RrsigType = (RecordType)46;
        private const RecordType Nsec3Type = (RecordType)50;

        public static IEnumerable<object[]> FixtureNames { get; } = new[]
        {
            "a",
            "aaaa",
            "a-rrsig-dnssec",
            "caa-unknown-type",
            "cname-chain",
            "cname-long-chain",
            "dnskey-dnssec",
            "https-unknown-type",
            "mx",
            "nodata",
            "ns-no-edns",
            "nxdomain-nsec3-dnssec",
            "nxdomain-soa",
            "ptr",
            "srv",
            "txt-multiple",
        }.Select(name => new object[] { name });

        [Theory]
        [MemberData(nameof(FixtureNames))]
        public void Request_SerializedBack_IsByteIdentical(string fixtureName)
        {
            var content = ReadFixture(fixtureName, "req");

            var request = Request.FromArray(content);
            var serialized = request.ToArray();

            Assert.Equal(content, serialized);
        }

        [Theory]
        [MemberData(nameof(FixtureNames))]
        public void Response_ParsedFromSerialized_HasSameContent(string fixtureName)
        {
            var content = ReadFixture(fixtureName, "resp");

            var original = Response.FromArray(content);
            var serialized = original.ToArray();
            var parsed = Response.FromArray(serialized);

            // Id, flags and section counts are not affected by name compression
            Assert.Equal(content[..Header.HeaderSize], serialized[..Header.HeaderSize]);
            AssertSameContent(original, parsed);
        }

        [Theory]
        [MemberData(nameof(FixtureNames))]
        public void Response_Serialized_IsNotLargerThanOriginal(string fixtureName)
        {
            // The upstream already fit the response into the payload size of the client
            var content = ReadFixture(fixtureName, "resp");

            var serialized = Response.FromArray(content).ToArray();

            Assert.True(serialized.Length <= content.Length, $"{serialized.Length} > {content.Length}");
        }

        [Theory]
        [MemberData(nameof(FixtureNames))]
        public void Response_SerializedTwice_IsByteIdentical(string fixtureName)
        {
            var content = ReadFixture(fixtureName, "resp");

            var firstSerialized = Response.FromArray(content).ToArray();
            var secondSerialized = Response.FromArray(firstSerialized).ToArray();

            Assert.Equal(firstSerialized, secondSerialized);
        }

        [Theory]
        [MemberData(nameof(FixtureNames))]
        public void Response_Truncated_Throws(string fixtureName)
        {
            var content = ReadFixture(fixtureName, "resp");

            for (var length = 0; length < content.Length; length++)
            {
                var truncated = content[..length];
                Assert.ThrowsAny<Exception>(() => Response.FromArray(truncated));
            }
        }

        [Theory]
        [MemberData(nameof(FixtureNames))]
        public void Request_Truncated_Throws(string fixtureName)
        {
            var content = ReadFixture(fixtureName, "req");

            for (var length = 0; length < content.Length; length++)
            {
                var truncated = content[..length];
                Assert.ThrowsAny<Exception>(() => Request.FromArray(truncated));
            }
        }

        [Fact]
        public void CompressedCanonicalNames_AreDecoded()
        {
            var response = ReadSerializedResponse("cname-long-chain");

            var canonicalNames = response.AnswerRecords
                .OfType<CanonicalNameResourceRecord>()
                .Select(record => $"{record.Name} -> {record.CanonicalDomainName}")
                .ToArray();
            var lastAnswer = response.AnswerRecords[^1];
            var address = Assert.IsType<IpAddressResourceRecord>(lastAnswer);

            Assert.Equal(
                new[]
                {
                    "www.microsoft.com -> www.microsoft.com-c-3.edgekey.net",
                    "www.microsoft.com-c-3.edgekey.net -> e13678.dscb.akamaiedge.net",
                },
                canonicalNames);
            Assert.Equal("e13678.dscb.akamaiedge.net", address.Name.ToString());
            Assert.Equal("23.12.158.97", address.IpAddress.ToString());
        }

        [Fact]
        public void CompressedMailExchangeNames_AreDecoded()
        {
            var response = ReadSerializedResponse("mx");

            var exchanges = response.AnswerRecords
                .Cast<MailExchangeResourceRecord>()
                .OrderBy(record => record.Preference)
                .Select(record => $"{record.Preference} {record.ExchangeDomainName}")
                .ToArray();

            Assert.Equal(
                new[]
                {
                    "5 gmail-smtp-in.l.google.com",
                    "10 alt1.gmail-smtp-in.l.google.com",
                    "20 alt2.gmail-smtp-in.l.google.com",
                    "30 alt3.gmail-smtp-in.l.google.com",
                    "40 alt4.gmail-smtp-in.l.google.com",
                },
                exchanges);
        }

        [Fact]
        public void CompressedServiceTarget_IsDecoded()
        {
            var response = ReadSerializedResponse("srv");

            var answer = Assert.Single(response.AnswerRecords);
            var service = Assert.IsType<ServiceResourceRecord>(answer);

            Assert.Equal("_xmpp-server._tcp.jabber.org", service.Name.ToString());
            Assert.Equal("scarlet.jabber.org", service.Target.ToString());
            Assert.Equal(30, service.Priority);
            Assert.Equal(30, service.Weight);
            Assert.Equal(5269, service.Port);
        }

        [Fact]
        public void CompressedPointerName_IsDecoded()
        {
            var response = ReadSerializedResponse("ptr");

            var answer = Assert.Single(response.AnswerRecords);
            var pointer = Assert.IsType<PointerResourceRecord>(answer);

            Assert.Equal("dns.google", pointer.PointerDomainName.ToString());
        }

        [Fact]
        public void CompressedStartOfAuthorityNames_AreDecoded()
        {
            var response = ReadSerializedResponse("nxdomain-soa");

            var authority = Assert.Single(response.AuthorityRecords);
            var soa = Assert.IsType<StartOfAuthorityResourceRecord>(authority);

            Assert.Equal(ResponseCode.NameError, response.ResponseCode);
            Assert.Equal("com", soa.Name.ToString());
            Assert.Equal("a.gtld-servers.net", soa.MasterDomainName.ToString());
            Assert.Equal("nstld.verisign-grs.com", soa.ResponsibleDomainName.ToString());
            Assert.Equal(1794090981, soa.SerialNumber);
            Assert.Equal(TimeSpan.FromSeconds(1800), soa.RefreshInterval);
            Assert.Equal(TimeSpan.FromSeconds(900), soa.RetryInterval);
            Assert.Equal(TimeSpan.FromSeconds(604800), soa.ExpireInterval);
            Assert.Equal(TimeSpan.FromSeconds(900), soa.MinimumTimeToLive);
        }

        [Fact]
        public void TextRecords_KeepAllCharacterStrings()
        {
            var content = ReadFixture("txt-multiple", "resp");
            var original = Response.FromArray(content);

            var parsed = ReadSerializedResponse("txt-multiple");

            var originalTexts = original.AnswerRecords
                .Cast<TextResourceRecord>()
                .Select(record => record.ToStringTextData());
            var parsedTexts = parsed.AnswerRecords
                .Cast<TextResourceRecord>()
                .Select(record => record.ToStringTextData());
            Assert.Equal(17, parsed.AnswerRecords.Count);
            Assert.Equal(originalTexts, parsedTexts);
            Assert.Contains("v=spf1 include:_spf.google.com ~all", parsedTexts);
        }

        [Fact]
        public void EdnsRecord_IsKept()
        {
            var response = ReadSerializedResponse("a-rrsig-dnssec");
            var requestContent = ReadFixture("a-rrsig-dnssec", "req");
            var request = Request.FromArray(requestContent);

            var requestOpt = Assert.Single(request.AdditionalRecords);
            var responseOpt = Assert.Single(response.AdditionalRecords);

            Assert.Equal(RecordType.OPT, requestOpt.Type);
            Assert.Equal(RecordType.OPT, responseOpt.Type);
            Assert.Equal(string.Empty, responseOpt.Name.ToString());
            // DO flag is stored in TTL of OPT record
            Assert.Equal(0x8000u, (uint)requestOpt.TimeToLive.TotalSeconds & 0x8000u);
        }

        [Fact]
        public void DnssecRecords_ArePassedAsIs()
        {
            var content = ReadFixture("nxdomain-nsec3-dnssec", "resp");
            var original = Response.FromArray(content);

            var parsed = ReadSerializedResponse("nxdomain-nsec3-dnssec");

            var types = parsed.AuthorityRecords.Select(record => record.Type).Distinct().ToArray();
            Assert.Equal(new[] { RecordType.SOA, RrsigType, Nsec3Type }, types);
            Assert.Equal(original.AuthorityRecords, parsed.AuthorityRecords, ResourceRecordComparer.Instance);
        }

        private static Response ReadSerializedResponse(string fixtureName)
        {
            var content = ReadFixture(fixtureName, "resp");
            var serialized = Response.FromArray(content).ToArray();
            return Response.FromArray(serialized);
        }

        private static byte[] ReadFixture(string fixtureName, string extension)
        {
            return Helper.ReadFixture(FixtureDirectory, $"{fixtureName}.{extension}");
        }

        private static void AssertSameContent(Response expected, Response actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.OperationCode, actual.OperationCode);
            Assert.Equal(expected.ResponseCode, actual.ResponseCode);
            Assert.Equal(expected.RecursionDesired, actual.RecursionDesired);
            Assert.Equal(expected.RecursionAvailable, actual.RecursionAvailable);
            Assert.Equal(expected.AuthorativeServer, actual.AuthorativeServer);
            Assert.Equal(expected.AuthenticData, actual.AuthenticData);
            Assert.Equal(expected.CheckingDisabled, actual.CheckingDisabled);
            Assert.Equal(expected.Truncated, actual.Truncated);

            Assert.Equal(expected.Questions, actual.Questions, QuestionComparer.Instance);
            AssertSameRecords(expected.AnswerRecords, actual.AnswerRecords);
            AssertSameRecords(expected.AuthorityRecords, actual.AuthorityRecords);
            AssertSameRecords(expected.AdditionalRecords, actual.AdditionalRecords);
        }

        private static void AssertSameRecords(IList<IResourceRecord> expected, IList<IResourceRecord> actual)
        {
            Assert.Equal(expected.Count, actual.Count);

            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].GetType(), actual[i].GetType());
                Assert.Equal(expected[i], actual[i], ResourceRecordComparer.Instance);
                // ResourceRecordComparer doesn't compare TTL
                Assert.Equal(expected[i].TimeToLive, actual[i].TimeToLive);
            }
        }
    }
}
