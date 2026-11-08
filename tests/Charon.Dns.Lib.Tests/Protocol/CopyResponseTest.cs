using Charon.Dns.Lib.Protocol;
using Xunit;

namespace Charon.Dns.Lib.Tests.Protocol
{
    public class CopyResponseTest
    {
        [Fact]
        public void FromRequest_CopiesRecursionDesired()
        {
            var request = new Request
            {
                RecursionDesired = true,
            };

            var response = Response.FromRequest(request);

            Assert.True(response.RecursionDesired);
        }

        [Fact]
        public void CopyConstructor_CopiesHeaderFlags()
        {
            var original = new Response
            {
                Id = 42,
                RecursionDesired = true,
                RecursionAvailable = true,
                AuthenticData = true,
                CheckingDisabled = true,
                AuthorativeServer = true,
                Truncated = true,
                OperationCode = OperationCode.Query,
                ResponseCode = ResponseCode.NameError,
            };

            var copy = new Response(original);

            Assert.Equal(42, copy.Id);
            Assert.True(copy.RecursionDesired);
            Assert.True(copy.RecursionAvailable);
            Assert.True(copy.AuthenticData);
            Assert.True(copy.CheckingDisabled);
            Assert.True(copy.AuthorativeServer);
            Assert.True(copy.Truncated);
            Assert.Equal(ResponseCode.NameError, copy.ResponseCode);
        }
    }
}
