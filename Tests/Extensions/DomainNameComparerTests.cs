using System.Collections.Generic;
using Charon.Dns.Extensions;
using Xunit;

namespace Tests.Extensions
{
    public class DomainNameComparerTests
    {
        [Theory]
        [InlineData(null, null)]
        [InlineData("example.com", "example.com")]
        [InlineData("Example.Com", "example.com")]
        [InlineData("example.com", "eXample.com")]
        [InlineData("Example.Com", "exAmple.com")]
        [InlineData("sub.example.com", "sub.example.com")]
        [InlineData("mail.example.com", "video.example.com")]
        [InlineData("Mail.Example.Com", "video.EXAMPLE.com")]
        [InlineData("example.com", "video.example.com")]
        [InlineData("chat.example.com", "video.example.com")]
        [InlineData("chat.example.com", "example.com")]
        public void CheckThatSameDomainNamesAreEqual(string domainName1, string domainName2)
        {
            var comparer = new DomainNameComparer();
            
            var result = comparer.Equals(domainName1, domainName2);
            
            Assert.True(result, $"{domainName1} != {domainName2}");
        }
        
        [Theory]
        [InlineData(null, "host.com")]
        [InlineData("example.com", "example.net")]
        [InlineData("sub.example.com", "sub.example.net")]
        [InlineData("example1.com", "example2.com")]
        [InlineData("hello.com", "olleh.com")]
        [InlineData("chat.hello.com", "chat.olleh.com")]
        public void CheckThatDifferentDomainNamesAreNotEqual(string domainName1, string domainName2)
        {
            var comparer = new DomainNameComparer();
            
            var result = comparer.Equals(domainName1, domainName2);
            
            Assert.False(result, $"{domainName1} == {domainName2}");
        }
        
        [Theory]
        [InlineData("example.com", "example.com")]
        [InlineData("Example.Com", "example.com")]
        [InlineData("example.com", "eXample.com")]
        [InlineData("Example.Com", "exAmple.com")]
        [InlineData("sub.example.com", "sub.example.com")]
        [InlineData("mail.example.com", "video.example.com")]
        [InlineData("Mail.Example.Com", "video.EXAMPLE.com")]
        [InlineData("example.com", "video.example.com")]
        [InlineData("chat.example.com", "video.example.com")]
        [InlineData("chat.example.com", "example.com")]
        public void CheckThatSameDomainNamesHasSameHashCodes(string domainName1, string domainName2)
        {
            var comparer = new DomainNameComparer();
            
            var hashCode1 = comparer.GetHashCode(domainName1);
            var hashCode2 = comparer.GetHashCode(domainName2);
            
            Assert.Equal(hashCode1, hashCode2);
        }
        
        [Theory]
        [InlineData("example.com", "example.net")]
        [InlineData("sub.example.com", "sub.example.net")]
        [InlineData("example1.com", "example2.com")]
        [InlineData("hello.com", "olleh.com")]
        [InlineData("chat.hello.com", "chat.olleh.com")]
        public void CheckThatDifferentDomainNamesHasDifferentHashCodes(string domainName1, string domainName2)
        {
            var comparer = new DomainNameComparer();
            
            var hashCode1 = comparer.GetHashCode(domainName1);
            var hashCode2 = comparer.GetHashCode(domainName2);
            
            Assert.NotEqual(hashCode1, hashCode2);
        }
        
        [Theory]
        [InlineData(null, null)]
        [InlineData("example.com", "example.com")]
        [InlineData("Example.Com", "example.com")]
        [InlineData("example.com", "eXample.com")]
        [InlineData("Example.Com", "exAmple.com")]
        [InlineData("sub.example.com", "sub.example.com")]
        [InlineData("mail.example.com", "video.example.com")]
        [InlineData("Mail.Example.Com", "video.EXAMPLE.com")]
        [InlineData("example.com", "video.example.com")]
        [InlineData("chat.example.com", "video.example.com")]
        [InlineData("chat.example.com", "example.com")]
        public void CheckThatComparerWorksCorrectlyInHashSet(string domainName1, string domainName2)
        {
            var set = new HashSet<string>(DomainNameComparer.Instance)
            {
                domainName1,
            };

            var result = set.Contains(domainName2);
            
            Assert.True(result);
        }
        
        [Theory]
        [InlineData("example.com", "example.net")]
        [InlineData("sub.example.com", "sub.example.net")]
        [InlineData("example1.com", "example2.com")]
        [InlineData("hello.com", "olleh.com")]
        [InlineData("chat.hello.com", "chat.olleh.com")]
        public void CheckThatComparerWorksCorrectlyInHashSet2(string domainName1, string domainName2)
        {
            var set = new HashSet<string>(DomainNameComparer.Instance)
            {
                domainName1,
            };

            var result = set.Contains(domainName2);
            
            Assert.False(result);
        }
    }
}
