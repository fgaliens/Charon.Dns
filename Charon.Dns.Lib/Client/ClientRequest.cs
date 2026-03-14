using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.EqualityComparers;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Client
{
    public class ClientRequest : IRequest
    {
        private const int DefaultPort = 53;

        private readonly IRequestResolver _resolver;
        private readonly IRequest _request;

        public ClientRequest(IPEndPoint dns, IRequest request = null) :
            this(new UdpRequestResolver(dns), request)
        { }

        public ClientRequest(IPAddress ip, int port = DefaultPort, IRequest request = null) :
            this(new IPEndPoint(ip, port), request)
        { }

        public ClientRequest(string ip, int port = DefaultPort, IRequest request = null) :
            this(IPAddress.Parse(ip), port, request)
        { }

        public ClientRequest(IRequestResolver resolver, IRequest request = null)
        {
            this._resolver = resolver;
            this._request = request == null ? new Request() : new Request(request);
        }

        public int Id
        {
            get { return _request.Id; }
            set { _request.Id = value; }
        }

        public IList<IResourceRecord> AdditionalRecords
        {
            get { return _request.AdditionalRecords; }
        }

        public OperationCode OperationCode
        {
            get { return _request.OperationCode; }
            set { _request.OperationCode = value; }
        }

        public bool RecursionDesired
        {
            get { return _request.RecursionDesired; }
            set { _request.RecursionDesired = value; }
        }

        public IList<Question> Questions
        {
            get { return _request.Questions; }
        }

        public int Size
        {
            get { return _request.Size; }
        }

        public byte[] ToArray()
        {
            return _request.ToArray();
        }

        public bool Equals(IRequest other)
        {
            return _request.Equals(other);
        }

        public override int GetHashCode()
        {
            return _request.GetHashCode();
        }

        public override string ToString()
        {
            return _request.ToString();
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
        public async Task<IResponse> Resolve(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                IResponse response = await _resolver.Resolve(this, cancellationToken).ConfigureAwait(false);

                if (response.Id != this.Id)
                {
                    throw new ResponseException(response, "Mismatching request/response IDs");
                }
                if (response.ResponseCode != ResponseCode.NoError)
                {
                    throw new ResponseException(response);
                }

                return response;
            }
            catch (ArgumentException e)
            {
                throw new ResponseException("Invalid response", e);
            }
            catch (IndexOutOfRangeException e)
            {
                throw new ResponseException("Invalid response", e);
            }
        }
    }
}
