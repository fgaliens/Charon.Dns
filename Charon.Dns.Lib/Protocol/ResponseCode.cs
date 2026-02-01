namespace Charon.Dns.Lib.Protocol
{
    public enum ResponseCode
    {
        NoError = 0,
        FormatError,
        ServerFailure,
        NameError,
        NotImplemented,
        Refused,
        YXDomain,
        YXRRSet,
        NXRRSet,
        NotAuth,
        NotZone,
    }
}
