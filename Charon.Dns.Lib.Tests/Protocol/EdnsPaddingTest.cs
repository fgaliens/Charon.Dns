#nullable enable
using System.Buffers.Binary;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Protocol.Utils;
using JetBrains.Annotations;
using Xunit;

namespace Charon.Dns.Lib.Tests.Protocol;

[TestSubject(typeof(EdnsPadding))]
public class EdnsPaddingTest
{
    private const ushort PaddingOptionCode = 12;
    private static readonly Domain RootName = new("");

    [Theory]
    [InlineData("a.ru")]
    [InlineData("a-much-longer-name.example.com")]
    public void PadRequest_PadsToMultipleOf128(string name)
    {
        var request = CreateRequest(name);
        request.AdditionalRecords.Add(new ResourceRecord(RootName, [], RecordType.OPT, (RecordClass)1232));

        var padded = EdnsPadding.PadRequest(request);

        Assert.Equal(0, padded.Size % 128);
        var opt = Assert.Single(padded.AdditionalRecords);
        Assert.Equal(PaddingOptionCode, BinaryPrimitives.ReadUInt16BigEndian(opt.Data));
        Assert.Equal((RecordClass)1232, opt.Class);
        // The original request is not changed
        Assert.Empty(Assert.Single(request.AdditionalRecords).Data);
    }

    [Fact]
    public void PadRequest_ReplacesExistingPadding()
    {
        var request = CreateRequest("a.ru");
        byte[] optData = [0, (byte)PaddingOptionCode, 0, 200, ..new byte[200]];
        request.AdditionalRecords.Add(new ResourceRecord(RootName, optData, RecordType.OPT, (RecordClass)1232));

        var padded = EdnsPadding.PadRequest(request);

        Assert.Equal(128, padded.Size);
    }

    [Fact]
    public void PadRequest_WithoutEdns_ReturnsRequestAsIs()
    {
        var request = CreateRequest("a.ru");

        var padded = EdnsPadding.PadRequest(request);

        Assert.Same(request, padded);
    }

    [Fact]
    public void PadRequest_KeepsHeaderFlags()
    {
        // Id 0x0102, RD and CD set, one question for "a.ru", one OPT record
        byte[] message =
        [
            0x01, 0x02, 0x01, 0x10, 0, 1, 0, 0, 0, 0, 0, 1,
            1, (byte)'a', 2, (byte)'r', (byte)'u', 0, 0, 1, 0, 1,
            0, 0, 41, 0x04, 0xD0, 0, 0, 0, 0, 0, 0,
        ];
        var request = Request.FromArray(message);

        var padded = EdnsPadding.PadRequest(request);

        var paddedMessage = padded.ToArray();
        Assert.Equal(0x01, paddedMessage[2]);
        Assert.Equal(0x10, paddedMessage[3]);
    }

    private static Request CreateRequest(string name)
    {
        var request = new Request
        {
            Id = 1,
            RecursionDesired = true,
        };
        var domain = new Domain(name);
        request.Questions.Add(new Question(domain));
        return request;
    }
}
