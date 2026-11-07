using System.Collections.Generic;
using System.Collections.ObjectModel;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Client
{
    public class ClientResponse : IResponse
    {
        private readonly IResponse _response;
        private readonly byte[] _message;

        public static ClientResponse FromArray(IRequest request, byte[] message)
        {
            var response = Response.FromArray(message);
            return new ClientResponse(request, response, message);
        }

        internal ClientResponse(IRequest request, IResponse response, byte[] message)
        {
            Request = request;

            _message = message;
            _response = response;
        }

        internal ClientResponse(IRequest request, IResponse response)
        {
            Request = request;

            _message = response.ToArray();
            _response = response;
        }

        public IRequest Request { get; }

        public int Id
        {
            get => _response.Id;
            set { }
        }

        public IList<IResourceRecord> AnswerRecords => 
            _response.AnswerRecords;

        public IList<IResourceRecord> AuthorityRecords => 
            new ReadOnlyCollection<IResourceRecord>(_response.AuthorityRecords);

        public IList<IResourceRecord> AdditionalRecords => 
            new ReadOnlyCollection<IResourceRecord>(_response.AdditionalRecords);

        public bool RecursionDesired
        {
            get => _response.RecursionDesired;
            set { }
        }

        public bool RecursionAvailable
        {
            get => _response.RecursionAvailable;
            set { }
        }

        public bool AuthenticData
        {
            get => _response.AuthenticData;
            set { }
        }

        public bool CheckingDisabled
        {
            get => _response.CheckingDisabled;
            set { }
        }

        public bool AuthorativeServer
        {
            get => _response.AuthorativeServer;
            set { }
        }

        public bool Truncated
        {
            get => _response.Truncated;
            set { }
        }

        public OperationCode OperationCode
        {
            get => _response.OperationCode;
            set { }
        }

        public ResponseCode ResponseCode
        {
            get => _response.ResponseCode;
            set { }
        }

        public IList<Question> Questions => 
            new ReadOnlyCollection<Question>(_response.Questions);

        IReadOnlyList<Question> IReadOnlyResponse.Questions => (IReadOnlyList<Question>)_response.Questions;
        IReadOnlyList<IResourceRecord> IReadOnlyResponse.AnswerRecords => (IReadOnlyList<IResourceRecord>)_response.AnswerRecords;
        IReadOnlyList<IResourceRecord> IReadOnlyResponse.AuthorityRecords => (IReadOnlyList<IResourceRecord>)_response.AuthorityRecords;
        IReadOnlyList<IResourceRecord> IReadOnlyResponse.AdditionalRecords => (IReadOnlyList<IResourceRecord>)_response.AdditionalRecords;

        public int Size => _message.Length;

        public byte[] ToArray()
        {
            return _message;
        }

        public override string ToString()
        {
            return _response.ToString();
        }
    }
}
