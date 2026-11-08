using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Lib.Client;

public class ClientRequest(IRequestResolver resolver, IRequest? request = null) : IRequest
{
    private readonly IRequest _request = request is null ? new Request() : new Request(request);

    public int Id
    {
        get => _request.Id;
        set => _request.Id = value;
    }

    public IList<IResourceRecord> AdditionalRecords => _request.AdditionalRecords;

    public OperationCode OperationCode
    {
        get => _request.OperationCode;
        set => _request.OperationCode = value;
    }

    public bool RecursionDesired
    {
        get => _request.RecursionDesired;
        set => _request.RecursionDesired = value;
    }

    public IList<Question> Questions => _request.Questions;

    IReadOnlyList<Question> IReadOnlyRequest.Questions => (IReadOnlyList<Question>)_request.Questions;
    IReadOnlyList<IResourceRecord> IReadOnlyRequest.AdditionalRecords => (IReadOnlyList<IResourceRecord>)_request.AdditionalRecords;

    public int Size => _request.Size;

    public byte[] ToArray()
    {
        return _request.ToArray();
    }

    public bool Equals(IRequest? other)
    {
        return _request.Equals(other);
    }

    public override int GetHashCode()
    {
        return _request.GetHashCode();
    }

    public override string ToString()
    {
        return _request.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Resolves this request into a response using the provided DNS information. The given
    /// request strategy is used to retrieve the response.
    /// </summary>
    /// <exception cref="ResponseException">Throw if a malformed response is received from the server</exception>
    /// <exception cref="IOException">Thrown if a IO error occurs</exception>
    /// <exception cref="SocketException">Thrown if the reading or writing to the socket fails</exception>
    /// <exception cref="OperationCanceledException">Thrown if reading or writing to the socket timeouts</exception>
    /// <returns>The response received from server</returns>
    public async Task<IResponse> Resolve(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await resolver.Resolve(this, RequestTrace.Empty, cancellationToken).ConfigureAwait(false);

            if (response.Id != Id)
            {
                throw new ResponseException(response, "Mismatching request/response IDs");
            }

            if (response.ResponseCode != ResponseCode.NoError)
            {
                throw new ResponseException(response);
            }

            return response;
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException)
        {
            throw new ResponseException("Invalid response", e);
        }
    }
}
