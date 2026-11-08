namespace Charon.Dns.Extensions
{
    public class DomainNameComparer : IEqualityComparer<string?>
    {
        public static DomainNameComparer Instance { get; } = new DomainNameComparer();
        
        public bool Equals(string? domainName1, string? domainName2)
        {
            if (domainName1 is null && domainName2 is null)
                return true;
            
            if  (domainName1 is null || domainName2 is null)
                return false;
            
            var domainNameLength = Math.Min(domainName1.Length, domainName2.Length);
            var dotsRemain = 1;
            for (var i = 1; i <= domainNameLength && dotsRemain >= 0; i++)
            {
                var c1 = char.ToLower(domainName1[^i]);
                var c2 = char.ToLower(domainName2[^i]);

                if (c1 != c2)
                {
                    return false;
                }

                if (c1 == '.')
                {
                    dotsRemain--;
                }
            }
            
            return true;
        }

        public int GetHashCode(string domainName)
        {
            var hashCode = new HashCode();
            var dotsRemain = 1;
            for (var i = domainName.Length - 1; i >= 0 && dotsRemain >= 0; i--)
            {
                var c = char.ToLower(domainName[i]);

                if (c == '.')
                {
                    dotsRemain--;
                    continue;
                }
                
                hashCode.Add(c);
            }
            
            return hashCode.ToHashCode();
        }
    }
}
