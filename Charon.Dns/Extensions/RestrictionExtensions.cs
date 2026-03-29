using System.Numerics;

namespace Charon.Dns.Extensions;

public static class RestrictionExtensions
{
    extension<TValue>(TValue value) where TValue : INumber<TValue>
    {
        public TValue RestrictNotLessThen(TValue minValue)
        {
            if (value < minValue)
            {
                return minValue;
            }
            return value;
        }
    }
}
