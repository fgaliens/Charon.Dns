using System;
using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.Lib.Client;

public class ResponseException : Exception
{
    public ResponseException() { }
    public ResponseException(string message) : base(message) { }
    public ResponseException(string message, Exception e) : base(message, e) { }

    public ResponseException(IResponse response) : this(response, Format(response)) { }

    public ResponseException(IResponse response, Exception e)
        : base(Format(response), e)
    {
        Response = response;
    }

    public ResponseException(IResponse response, string message)
        : base(message)
    {
        Response = response;
    }

    public IResponse? Response { get; }

    private static string Format(IResponse response)
    {
        return $"Invalid response received with code {response.ResponseCode}";
    }
}
