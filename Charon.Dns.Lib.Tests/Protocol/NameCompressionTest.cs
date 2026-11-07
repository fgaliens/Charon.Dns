#nullable enable
using System;
using System.Linq;
using System.Net;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.EqualityComparers;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Xunit;

namespace Charon.Dns.Lib.Tests.Protocol
{
    public class NameCompressionTest
    {
        private const int HeaderAndQuestionNameOffset = Header.HeaderSize;
        private static readonly Domain Name = new("www.my.home");
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

        [Fact]
        public void RepeatedOwnerName_IsWrittenAsPointerToQuestion()
        {
            var response = CreateResponse(Name);
            var firstAddress = IPAddress.Parse("10.0.0.1");
            var secondAddress = IPAddress.Parse("10.0.0.2");
            var firstRecord = new IpAddressResourceRecord(Name, firstAddress, Ttl);
            var secondRecord = new IpAddressResourceRecord(Name, secondAddress, Ttl);
            response.AnswerRecords.Add(firstRecord);
            response.AnswerRecords.Add(secondRecord);

            var serialized = response.ToArray();

            var questionEnd = HeaderAndQuestionNameOffset + Name.Size + 4;
            Assert.Equal(0xC0, serialized[questionEnd]);
            Assert.Equal(HeaderAndQuestionNameOffset, serialized[questionEnd + 1]);
            Assert.Equal(serialized.Length, response.Size);
            AssertParsesBack(response, serialized);
        }

        [Fact]
        public void CommonSuffix_IsWrittenAsPointer()
        {
            var response = CreateResponse(Name);
            var otherName = new Domain("mail.my.home");
            var address = IPAddress.Parse("10.0.0.1");
            var record = new IpAddressResourceRecord(otherName, address, Ttl);
            response.AnswerRecords.Add(record);

            var serialized = response.ToArray();
            var uncompressedSize = Header.HeaderSize + Name.Size + 4 + otherName.Size + 10 + 4;
            // "mail" label and a pointer to "my.home" of the question
            var compressedOwnerSize = 1 + "mail".Length + 2;

            Assert.Equal(uncompressedSize - otherName.Size + compressedOwnerSize, serialized.Length);
            AssertParsesBack(response, serialized);
        }

        [Theory]
        [InlineData(RecordType.CNAME)]
        [InlineData(RecordType.NS)]
        [InlineData(RecordType.PTR)]
        public void NameInRecordData_IsCompressed(RecordType recordType)
        {
            var target = new Domain("target.my.home");
            var response = CreateResponse(Name);
            var record = CreateNameRecord(recordType, target);
            response.AnswerRecords.Add(record);

            var serialized = response.ToArray();
            var uncompressedSize = Header.HeaderSize + Name.Size + 4 + record.Size;
            var expectedData = target.ToArray();

            Assert.True(serialized.Length < uncompressedSize);
            var parsed = AssertParsesBack(response, serialized);
            var parsedRecord = Assert.Single(parsed.AnswerRecords);
            Assert.Equal(expectedData, parsedRecord.Data);
        }

        [Fact]
        public void MailExchangeAndStartOfAuthorityNames_AreCompressed()
        {
            var mailExchange = new Domain("mail.my.home");
            var master = new Domain("ns.my.home");
            var responsible = new Domain("hostmaster.my.home");
            var response = CreateResponse(Name);
            var mailExchangeRecord = new MailExchangeResourceRecord(Name, 10, mailExchange, Ttl);
            var soa = new StartOfAuthorityResourceRecord(Name, master, responsible, 7,
                TimeSpan.FromHours(1), TimeSpan.FromMinutes(10), TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), Ttl);
            response.AnswerRecords.Add(mailExchangeRecord);
            response.AuthorityRecords.Add(soa);

            var serialized = response.ToArray();
            var parsed = AssertParsesBack(response, serialized);

            var parsedMailExchange = Assert.IsType<MailExchangeResourceRecord>(parsed.AnswerRecords[0]);
            var parsedSoa = Assert.IsType<StartOfAuthorityResourceRecord>(parsed.AuthorityRecords[0]);
            Assert.Equal(10, parsedMailExchange.Preference);
            Assert.Equal(mailExchange, parsedMailExchange.ExchangeDomainName);
            Assert.Equal(master, parsedSoa.MasterDomainName);
            Assert.Equal(responsible, parsedSoa.ResponsibleDomainName);
            Assert.Equal(7, parsedSoa.SerialNumber);
            Assert.Equal(TimeSpan.FromDays(1), parsedSoa.ExpireInterval);
        }

        [Fact]
        public void ServiceTarget_IsNotCompressed()
        {
            // RFC 2782: the target name must not be compressed
            var target = new Domain("sip.my.home");
            var response = CreateResponse(Name);
            var record = new ServiceResourceRecord(Name, 1, 2, 5060, target, Ttl);
            response.AnswerRecords.Add(record);

            var serialized = response.ToArray();

            var targetBytes = target.ToArray();
            Assert.True(ContainsSequence(serialized, targetBytes));
            AssertParsesBack(response, serialized);
        }

        [Fact]
        public void UnknownRecordData_IsWrittenAsIs()
        {
            var data = Name.ToArray();
            var response = CreateResponse(Name);
            var record = new ResourceRecord(Name, data, (RecordType)65, RecordClass.IN, Ttl);
            response.AnswerRecords.Add(record);

            var serialized = response.ToArray();

            Assert.Equal(data, serialized[^data.Length..]);
            AssertParsesBack(response, serialized);
        }

        [Fact]
        public void NamesDifferentByCase_AreNotMerged()
        {
            // Some resolvers randomize the case of a question (DNS 0x20) and expect it back exactly
            var question = new Domain("WwW.My.HoMe");
            var response = CreateResponse(question);
            var address = IPAddress.Parse("10.0.0.1");
            var record = new IpAddressResourceRecord(Name, address, Ttl);
            response.AnswerRecords.Add(record);

            var serialized = response.ToArray();
            var parsed = Response.FromArray(serialized);

            Assert.Equal("WwW.My.HoMe", parsed.Questions[0].Name.ToString());
            Assert.Equal("www.my.home", parsed.AnswerRecords[0].Name.ToString());
        }

        [Fact]
        public void NamesAfterPointerOffsetLimit_AreStillCorrect()
        {
            // Pointer offsets are limited by 14 bits (16383)
            var response = CreateResponse(Name);
            var text = new string('x', 250);
            var characterStrings = CharacterString.FromString(text);
            for (var i = 0; i < 80; i++)
            {
                var textName = new Domain($"t{i}.my.home");
                var textRecord = new TextResourceRecord(textName, characterStrings, Ttl);
                response.AnswerRecords.Add(textRecord);
            }

            var lateName = new Domain("late.example.org");
            var address = IPAddress.Parse("10.0.0.1");
            var firstLateRecord = new IpAddressResourceRecord(lateName, address, Ttl);
            var secondLateRecord = new IpAddressResourceRecord(lateName, address, Ttl);
            response.AdditionalRecords.Add(firstLateRecord);
            response.AdditionalRecords.Add(secondLateRecord);

            var serialized = response.ToArray();

            Assert.True(serialized.Length > 0x3FFF);
            AssertParsesBack(response, serialized);
        }

        [Fact]
        public void Size_MatchesSerializedLength()
        {
            var response = CreateResponse(Name);
            var target = new Domain("target.my.home");
            var record = new CanonicalNameResourceRecord(Name, target, Ttl);
            response.AnswerRecords.Add(record);

            var size = response.Size;
            var destination = new byte[size];
            response.WriteTo(destination);

            var expected = response.ToArray();
            Assert.Equal(expected.Length, size);
            Assert.Equal(expected, destination);
        }

        [Theory]
        [InlineData("my.home", "my.home")]
        [InlineData("my.home.", "my.home")]
        [InlineData("", "")]
        [InlineData(".", "")]
        public void Domain_FromString(string value, string expected)
        {
            var domain = new Domain(value);

            Assert.Equal(expected, domain.ToString());
            Assert.Equal(expected.Length == 0 ? 1 : expected.Length + 2, domain.Size);
        }

        [Fact]
        public void Domain_RootIsSerializedAsSingleZero()
        {
            var domain = new Domain("");

            var serialized = domain.ToArray();

            Assert.Equal(new byte[] { 0 }, serialized);
        }

        [Theory]
        [InlineData("a..b")]
        [InlineData(".a")]
        [InlineData("a..")]
        public void Domain_WithEmptyLabel_Throws(string value)
        {
            Assert.Throws<ArgumentException>(() => new Domain(value));
        }

        [Fact]
        public void Domain_LabelLengthIsLimited()
        {
            var maxLabel = new string('a', Domain.MaxLabelLength);
            var tooLongLabel = new string('a', Domain.MaxLabelLength + 1);

            var domain = new Domain($"{maxLabel}.home");

            var serialized = domain.ToArray();

            Assert.Equal(Domain.MaxLabelLength, serialized[0]);
            Assert.Throws<ArgumentException>(() => new Domain($"{tooLongLabel}.home"));
        }

        [Fact]
        public void Domain_SizeIsLimited()
        {
            // 4 labels of 63 bytes: 4 * 64 + 1 = 257 bytes
            var label = new string('a', Domain.MaxLabelLength);
            var labels = Enumerable.Repeat(label, 4);
            var tooLongName = string.Join(".", labels);
            // 3 labels of 63 bytes and one of 61 bytes: 3 * 64 + 62 + 1 = 255 bytes
            var maxLabels = Enumerable.Repeat(label, 3).Append(new string('a', 61));
            var maxName = string.Join(".", maxLabels);

            var domain = new Domain(maxName);

            Assert.Equal(Domain.MaxNameSize, domain.Size);
            Assert.Throws<ArgumentException>(() => new Domain(tooLongName));
        }

        [Fact]
        public void Domain_ParsedWithTooLongLabel_Throws()
        {
            var tooLongLabel = Enumerable.Repeat((byte)'a', Domain.MaxLabelLength + 1).ToArray();
            byte[] message = [(byte)tooLongLabel.Length, ..tooLongLabel, 0];

            Assert.Throws<ArgumentException>(() => Domain.FromArray(message, 0));
        }

        private static Response CreateResponse(Domain questionName)
        {
            var response = new Response
            {
                Id = 7,
                RecursionDesired = true,
                RecursionAvailable = true,
            };
            var question = new Question(questionName);
            response.Questions.Add(question);

            return response;
        }

        private static IResourceRecord CreateNameRecord(RecordType recordType, Domain target)
        {
            return recordType switch
            {
                RecordType.CNAME => new CanonicalNameResourceRecord(Name, target, Ttl),
                RecordType.NS => new NameServerResourceRecord(Name, target, Ttl),
                RecordType.PTR => new PointerResourceRecord(Name, target, Ttl),
                _ => throw new ArgumentOutOfRangeException(nameof(recordType)),
            };
        }

        private static Response AssertParsesBack(Response expected, byte[] serialized)
        {
            var parsed = Response.FromArray(serialized);

            Assert.Equal(expected.Questions, parsed.Questions, QuestionComparer.Instance);
            Assert.Equal(expected.AnswerRecords, parsed.AnswerRecords, ResourceRecordComparer.Instance);
            Assert.Equal(expected.AuthorityRecords, parsed.AuthorityRecords, ResourceRecordComparer.Instance);
            Assert.Equal(expected.AdditionalRecords, parsed.AdditionalRecords, ResourceRecordComparer.Instance);
            Assert.Equal(serialized, parsed.ToArray());

            return parsed;
        }

        private static bool ContainsSequence(byte[] source, byte[] sequence)
        {
            return source.AsSpan().IndexOf(sequence) >= 0;
        }
    }
}
