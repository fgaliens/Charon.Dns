using System;

namespace Charon.Dns.Lib.Protocol.Marshalling
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Struct)]
    public class EndianAttribute : Attribute
    {
        public EndianAttribute(Endianness endianness)
        {
            this.Endianness = endianness;
        }

        public Endianness Endianness { get; }
    }
}
