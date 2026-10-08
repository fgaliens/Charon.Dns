using Charon.Dns.Lib.Protocol.Marshalling;
using Xunit;

namespace Charon.Dns.Lib.Tests.Protocol.Marshalling;

public class StructTest
{
    [Theory]
    [InlineData(0, false, 0, 0)]
    [InlineData(int.MaxValue, true, byte.MaxValue, ushort.MaxValue)]
    [InlineData(-1, true, 1, 1)]
    public void GetBytesThenGetStructRoundTrips(int field1, bool field2, byte field3, ushort field4)
    {
        var value = new TestStruct { Field1 = field1, Field2 = field2, Field3 = field3, Field4 = field4 };

        var bytes = Struct.GetBytes(value);
        var result = Struct.GetStruct<TestStruct>(bytes);

        Assert.Equal(value.Field1, result.Field1);
        Assert.Equal(value.Field2, result.Field2);
        Assert.Equal(value.Field3, result.Field3);
        Assert.Equal(value.Field4, result.Field4);
    }

    [Fact]
    public void GetBytesWritesIntoCallerSuppliedSpan()
    {
        var value = new TestStruct { Field1 = 0x1122_3344, Field2 = true, Field3 = 0xAB, Field4 = 0xBEEF };
        var destination = new byte[Struct.GetBytes(value).Length];

        Struct.GetBytes(value, destination);
        var result = Struct.GetStruct<TestStruct>(destination);

        Assert.Equal(value.Field1, result.Field1);
        Assert.Equal(value.Field2, result.Field2);
        Assert.Equal(value.Field3, result.Field3);
        Assert.Equal(value.Field4, result.Field4);
    }

    public struct TestStruct
    {
        public int Field1;
        public bool Field2;
        public byte Field3;
        public ushort Field4;
    }
}
