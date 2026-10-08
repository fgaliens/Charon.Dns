using System;
using System.Collections.Generic;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Xunit;

namespace Charon.Dns.Lib.Tests.Protocol.ResourceRecords
{

    public class SerializeResourceRecordTest
    {
        [Fact]
        public void BasicResourceRecordWithEmptyDomain()
        {
            byte[] content = Helper.ReadFixture("ResourceRecord", "empty-domain_basic");

            Domain domain = new Domain(Helper.GetArray<string>());
            ResourceRecord record = new ResourceRecord(domain, Helper.GetArray<byte>(),
                RecordType.A, RecordClass.IN, new TimeSpan(0));

            Assert.Equal(content, record.ToArray());
        }

        [Fact]
        public void BasicResourceRecordWithMultipleLabelDomain()
        {
            byte[] content = Helper.ReadFixture("ResourceRecord", "www.google.com_basic");

            Domain domain = new Domain(Helper.GetArray("www", "google", "com"));
            ResourceRecord record = new ResourceRecord(domain, Helper.GetArray<byte>(),
                RecordType.A, RecordClass.IN, new TimeSpan(0));

            Assert.Equal(content, record.ToArray());
        }

        [Fact]
        public void DataResourceRecordWithEmptyDomain()
        {
            byte[] content = Helper.ReadFixture("ResourceRecord", "empty-domain_data");

            byte[] data = Helper.GetArray<byte>(1, 1);
            Domain domain = new Domain(Helper.GetArray<string>());
            ResourceRecord record = new ResourceRecord(domain, data,
                RecordType.A, RecordClass.IN, new TimeSpan());

            Assert.Equal(content, record.ToArray());
        }

        [Fact]
        public void CNameResourceRecordWithEmptyDomain()
        {
            byte[] content = Helper.ReadFixture("ResourceRecord", "empty-domain_cname");

            Domain domain = new Domain(Helper.GetArray<string>());
            ResourceRecord record = new ResourceRecord(domain, Helper.GetArray<byte>(),
                RecordType.CNAME, RecordClass.IN, new TimeSpan(0));

            Assert.Equal(content, record.ToArray());
        }

        [Fact]
        public void AnyResourceRecordWithEmptyDomain()
        {
            byte[] content = Helper.ReadFixture("ResourceRecord", "empty-domain_any");

            Domain domain = new Domain(Helper.GetArray<string>());
            ResourceRecord record = new ResourceRecord(domain, Helper.GetArray<byte>(),
                RecordType.A, RecordClass.ANY, new TimeSpan(0));

            Assert.Equal(content, record.ToArray());
        }

        [Fact]
        public void TtlResourceRecordWithEmptyDomain()
        {
            byte[] content = Helper.ReadFixture("ResourceRecord", "empty-domain_ttl");

            Domain domain = new Domain(Helper.GetArray<string>());
            ResourceRecord record = new ResourceRecord(domain, Helper.GetArray<byte>(),
                RecordType.A, RecordClass.IN, TimeSpan.FromSeconds(1));

            Assert.Equal(content, record.ToArray());
        }

        [Fact]
        public void AllSetResourceRecordWithMultipleLabelDomain()
        {
            byte[] content = Helper.ReadFixture("ResourceRecord", "www.google.com_all");

            byte[] data = Helper.GetArray<byte>(1, 1);
            Domain domain = new Domain(Helper.GetArray("www", "google", "com"));
            ResourceRecord record = new ResourceRecord(domain, data,
                RecordType.CNAME, RecordClass.ANY, TimeSpan.FromSeconds(1));

            Assert.Equal(content, record.ToArray());
        }

        [Fact]
        public void SrvResourceRecordWithEmptyDomain()
        {
            byte[] content = Helper.ReadFixture("ResourceRecord", "empty-domain_srv");

            Domain domain = new Domain(Helper.GetArray<string>());
            Domain target = new Domain(Helper.GetArray("example", "com"));
            ServiceResourceRecord srv = new ServiceResourceRecord(domain, 10, 60, 8080, target, new TimeSpan(0));

            Assert.Equal(content, srv.ToArray());
        }

        [Fact]
        public void MxResourceRecordWithEmptyDomainAndSingleLabelExchange()
        {
            Domain domain = new Domain(Helper.GetArray<string>());
            Domain exchange = new Domain(Helper.GetArray("a"));
            MailExchangeResourceRecord mx = new MailExchangeResourceRecord(domain, 10, exchange, new TimeSpan(0));

            byte[] expected = Helper.GetArray<byte>(
                0,                   // empty domain name (root)
                0, 15,                // Type = MX
                0, 1,                 // Class = IN
                0, 0, 0, 0,           // TTL = 0
                0, 5,                 // DataLength = 2 (preference) + 3 (exchange "a")
                0, 10,                // Preference
                1, (byte)'a', 0       // Exchange domain wire bytes
            );

            Assert.Equal(expected, mx.ToArray());
        }

        [Fact]
        public void SoaResourceRecordWithEmptyDomainAndSingleLabelMasterResponsible()
        {
            Domain domain = new Domain(Helper.GetArray<string>());
            Domain master = new Domain(Helper.GetArray("a"));
            Domain responsible = new Domain(Helper.GetArray("b"));
            StartOfAuthorityResourceRecord soa = new StartOfAuthorityResourceRecord(
                domain, master, responsible, 1,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5),
                new TimeSpan(0));

            byte[] expected = Helper.GetArray<byte>(
                0,                    // empty domain name (root)
                0, 6,                 // Type = SOA
                0, 1,                 // Class = IN
                0, 0, 0, 0,           // TTL = 0
                0, 26,                // DataLength = 3 (master) + 3 (responsible) + 20 (options)
                1, (byte)'a', 0,      // master domain wire bytes
                1, (byte)'b', 0,      // responsible domain wire bytes
                0, 0, 0, 1,           // serial
                0, 0, 0, 2,           // refresh
                0, 0, 0, 3,           // retry
                0, 0, 0, 4,           // expire
                0, 0, 0, 5            // minimum ttl
            );

            Assert.Equal(expected, soa.ToArray());
        }

        [Fact]
        public void TxtResourceRecordWithEmptyDomainAndSingleCharacterString()
        {
            Domain domain = new Domain(Helper.GetArray<string>());
            IList<CharacterString> text = CharacterString.FromString("hello");
            TextResourceRecord txt = new TextResourceRecord(domain, text, new TimeSpan(0));

            byte[] expected = Helper.GetArray<byte>(
                0,                    // empty domain name (root)
                0, 16,                // Type = TXT
                0, 1,                 // Class = IN
                0, 0, 0, 0,           // TTL = 0
                0, 6,                 // DataLength = 1 (length byte) + 5 ("hello")
                5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o'
            );

            Assert.Equal(expected, txt.ToArray());
        }
    }
}
