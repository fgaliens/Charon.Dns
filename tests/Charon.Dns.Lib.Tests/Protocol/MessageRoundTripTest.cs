#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.EqualityComparers;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Xunit;

namespace Charon.Dns.Lib.Tests.Protocol
{
    public class MessageRoundTripTest
    {
        private static readonly Domain Name = new("my.home");
        private static readonly Domain RootName = new(Array.Empty<string>());
        private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(3600);

        [Theory]
        [InlineData("10.0.0.1")]
        [InlineData("fd00::1")]
        public void IpAddressRecord(string address)
        {
            var ipAddress = IPAddress.Parse(address);
            var record = new IpAddressResourceRecord(Name, ipAddress, Ttl);

            var parsed = RoundTrip<IpAddressResourceRecord>(record);

            Assert.Equal(ipAddress, parsed.IpAddress);
        }

        [Fact]
        public void CanonicalNameRecord()
        {
            var canonicalName = new Domain("target.example.com");
            var record = new CanonicalNameResourceRecord(Name, canonicalName, Ttl);

            var parsed = RoundTrip<CanonicalNameResourceRecord>(record);

            Assert.Equal(canonicalName, parsed.CanonicalDomainName);
        }

        [Fact]
        public void NameServerRecord()
        {
            var nameServer = new Domain("ns1.my.home");
            var record = new NameServerResourceRecord(Name, nameServer, Ttl);

            var parsed = RoundTrip<NameServerResourceRecord>(record);

            Assert.Equal(nameServer, parsed.NSDomainName);
        }

        [Fact]
        public void PointerRecord()
        {
            var address = IPAddress.Parse("10.0.0.1");
            var pointer = new Domain("host.my.home");
            var record = new PointerResourceRecord(address, pointer, Ttl);

            var parsed = RoundTrip<PointerResourceRecord>(record);

            Assert.Equal("1.0.0.10.in-addr.arpa", parsed.Name.ToString());
            Assert.Equal(pointer, parsed.PointerDomainName);
        }

        [Fact]
        public void MailExchangeRecord()
        {
            var exchange = new Domain("mail.my.home");
            var record = new MailExchangeResourceRecord(Name, 10, exchange, Ttl);

            var parsed = RoundTrip<MailExchangeResourceRecord>(record);

            Assert.Equal(10, parsed.Preference);
            Assert.Equal(exchange, parsed.ExchangeDomainName);
        }

        [Fact]
        public void StartOfAuthorityRecord()
        {
            var master = new Domain("ns.my.home");
            var responsible = new Domain("hostmaster.my.home");
            var record = new StartOfAuthorityResourceRecord(
                Name,
                master,
                responsible,
                uint.MaxValue,
                TimeSpan.FromHours(1),
                TimeSpan.FromMinutes(10),
                TimeSpan.FromDays(7),
                TimeSpan.FromMinutes(5),
                Ttl);

            var parsed = RoundTrip<StartOfAuthorityResourceRecord>(record);

            Assert.Equal(master, parsed.MasterDomainName);
            Assert.Equal(responsible, parsed.ResponsibleDomainName);
            Assert.Equal(uint.MaxValue, parsed.SerialNumber);
            Assert.Equal(TimeSpan.FromHours(1), parsed.RefreshInterval);
            Assert.Equal(TimeSpan.FromMinutes(10), parsed.RetryInterval);
            Assert.Equal(TimeSpan.FromDays(7), parsed.ExpireInterval);
            Assert.Equal(TimeSpan.FromMinutes(5), parsed.MinimumTimeToLive);
        }

        [Fact]
        public void ServiceRecord()
        {
            var target = new Domain("sip.my.home");
            var record = new ServiceResourceRecord(Name, 10, 5, ushort.MaxValue, target, Ttl);

            var parsed = RoundTrip<ServiceResourceRecord>(record);

            Assert.Equal(10, parsed.Priority);
            Assert.Equal(5, parsed.Weight);
            Assert.Equal(ushort.MaxValue, parsed.Port);
            Assert.Equal(target, parsed.Target);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(255)]
        [InlineData(256)]
        [InlineData(1000)]
        public void TextRecord_SplitIntoCharacterStrings(int textLength)
        {
            var text = new string('x', textLength);
            var characterStrings = CharacterString.FromString(text);
            var record = new TextResourceRecord(Name, characterStrings, Ttl);

            var parsed = RoundTrip<TextResourceRecord>(record);

            Assert.Equal(characterStrings.Count, parsed.TextData.Count);
            Assert.Equal(text, parsed.ToStringTextData());
        }

        [Fact]
        public void UnknownRecord_DataIsPassedAsIs()
        {
            const RecordType caaType = (RecordType)257;
            byte[] data = [0, 5, .."issue"u8, .."letsencrypt.org"u8];
            var record = new ResourceRecord(Name, data, caaType, RecordClass.IN, Ttl);

            var parsed = RoundTrip<ResourceRecord>(record);

            Assert.Equal(caaType, parsed.Type);
            Assert.Equal(data, parsed.Data);
        }

        [Fact]
        public void EdnsRecord_KeepsPayloadSizeFlagsAndOptions()
        {
            const uint dnssecOkFlag = 0x8000;
            const RecordClass payloadSize = (RecordClass)1232;
            var flagsAsTtl = TimeSpan.FromSeconds(dnssecOkFlag);
            byte[] cookieOption = [0, 10, 0, 8, 1, 2, 3, 4, 5, 6, 7, 8];
            var opt = new ResourceRecord(RootName, cookieOption, RecordType.OPT, payloadSize, flagsAsTtl);

            var request = CreateRequest();
            request.AdditionalRecords.Add(opt);
            var serialized = request.ToArray();
            var parsed = Request.FromArray(serialized);

            var parsedOpt = Assert.Single(parsed.AdditionalRecords);
            Assert.Equal(RecordType.OPT, parsedOpt.Type);
            Assert.Equal(payloadSize, parsedOpt.Class);
            Assert.Equal(flagsAsTtl, parsedOpt.TimeToLive);
            Assert.Equal(cookieOption, parsedOpt.Data);
            Assert.Equal(string.Empty, parsedOpt.Name.ToString());
        }

        [Fact]
        public void Response_WithRecordsInAllSections()
        {
            var address = IPAddress.Parse("10.0.0.1");
            var answer = new IpAddressResourceRecord(Name, address, Ttl);
            var soa = new StartOfAuthorityResourceRecord(Name, Name, Name, ttl: Ttl);
            var opt = new ResourceRecord(RootName, [], RecordType.OPT, (RecordClass)1232);
            var request = CreateRequest();
            var response = Response.FromRequest(request);
            response.AnswerRecords.Add(answer);
            response.AuthorityRecords.Add(soa);
            response.AdditionalRecords.Add(opt);

            var serialized = response.ToArray();
            var parsed = Response.FromArray(serialized);

            Assert.Equal(response.Size, serialized.Length);
            Assert.Equal(response.Questions, parsed.Questions, QuestionComparer.Instance);
            Assert.Equal(response.AnswerRecords, parsed.AnswerRecords, ResourceRecordComparer.Instance);
            Assert.Equal(response.AuthorityRecords, parsed.AuthorityRecords, ResourceRecordComparer.Instance);
            Assert.Equal(response.AdditionalRecords, parsed.AdditionalRecords, ResourceRecordComparer.Instance);
        }

        [Fact]
        public void Request_WithMultipleQuestions()
        {
            var request = CreateRequest();
            var otherName = new Domain("other.my.home");
            var otherQuestion = new Question(otherName, RecordType.AAAA, RecordClass.ANY);
            request.Questions.Add(otherQuestion);

            var serialized = request.ToArray();
            var parsed = Request.FromArray(serialized);

            Assert.Equal(request.Id, parsed.Id);
            Assert.Equal(request.Questions, parsed.Questions, QuestionComparer.Instance);
            Assert.Equal(serialized, parsed.ToArray());
        }

        public static IEnumerable<object[]> HeaderFlags()
        {
            for (var flags = 0; flags < 1 << 6; flags++)
            {
                yield return [flags];
            }
        }

        [Theory]
        [MemberData(nameof(HeaderFlags))]
        public void Response_HeaderFlags(int flags)
        {
            var response = new Response
            {
                Id = ushort.MaxValue,
                AuthorativeServer = (flags & 1) != 0,
                Truncated = (flags & 2) != 0,
                RecursionDesired = (flags & 4) != 0,
                RecursionAvailable = (flags & 8) != 0,
                AuthenticData = (flags & 16) != 0,
                CheckingDisabled = (flags & 32) != 0,
                OperationCode = OperationCode.Status,
                ResponseCode = ResponseCode.Refused,
            };
            var question = new Question(Name);
            response.Questions.Add(question);

            var serialized = response.ToArray();
            var parsed = Response.FromArray(serialized);

            Assert.Equal(ushort.MaxValue, parsed.Id);
            Assert.Equal(response.AuthorativeServer, parsed.AuthorativeServer);
            Assert.Equal(response.Truncated, parsed.Truncated);
            Assert.Equal(response.RecursionDesired, parsed.RecursionDesired);
            Assert.Equal(response.RecursionAvailable, parsed.RecursionAvailable);
            Assert.Equal(response.AuthenticData, parsed.AuthenticData);
            Assert.Equal(response.CheckingDisabled, parsed.CheckingDisabled);
            Assert.Equal(OperationCode.Status, parsed.OperationCode);
            Assert.Equal(ResponseCode.Refused, parsed.ResponseCode);
        }

        [Fact]
        public void HeaderComparer_WithId_ComparesId()
        {
            var header = new Header { Id = 1, RecursionDesired = true };
            var sameHeader = new Header { Id = 1, RecursionDesired = true };
            var otherIdHeader = new Header { Id = 2, RecursionDesired = true };

            Assert.True(HeaderComparer.InstanceWithIdComparision.Equals(header, sameHeader));
            Assert.False(HeaderComparer.InstanceWithIdComparision.Equals(header, otherIdHeader));
            Assert.True(HeaderComparer.InstanceWithoutIdComparision.Equals(header, otherIdHeader));
        }

        private static T RoundTrip<T>(IResourceRecord record) where T : IResourceRecord
        {
            var request = CreateRequest();
            var response = Response.FromRequest(request);
            response.AnswerRecords.Add(record);

            var serialized = response.ToArray();
            var parsed = Response.FromArray(serialized);

            var parsedRecord = Assert.Single(parsed.AnswerRecords);
            Assert.Equal(record, parsedRecord, ResourceRecordComparer.Instance);
            Assert.Equal(record.TimeToLive, parsedRecord.TimeToLive);
            Assert.Equal(serialized, parsed.ToArray());

            return Assert.IsType<T>(parsedRecord);
        }

        private static Request CreateRequest()
        {
            var request = new Request
            {
                Id = 4242,
                RecursionDesired = true,
            };
            var question = new Question(Name);
            request.Questions.Add(question);

            return request;
        }
    }
}
