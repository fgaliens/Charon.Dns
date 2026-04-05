#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol
{
    // TODO: Beware of mutations. Think about readonly request and response
    public class Response : IResponse
    {
        private Header _header;
        private readonly IList<Question> _questions;
        private readonly IList<IResourceRecord> _answers;
        private readonly IList<IResourceRecord> _authority;
        private readonly IList<IResourceRecord> _additional;
        private readonly byte[]? _originalResponse;

        public static Response FromRequest(IRequest request)
        {
            Response response = new Response();

            response.Id = request.Id;

            foreach (Question question in request.Questions)
            {
                response.Questions.Add(question);
            }

            return response;
        }

        public static Response FromArray(byte[] message)
        {
            Header header = Header.FromArray(message);
            int offset = header.Size;

            if (!header.Response)
            {
                throw new ArgumentException("Invalid response message");
            }

            if (header.Truncated)
            {
                return new Response(header,
                    Question.GetAllFromArray(message, offset, header.QuestionCount),
                    new List<IResourceRecord>(),
                    new List<IResourceRecord>(),
                    new List<IResourceRecord>());
            }

            return new Response(header,
                Question.GetAllFromArray(message, offset, header.QuestionCount, out offset),
                ResourceRecordFactory.GetAllFromArray(message, offset, header.AnswerRecordCount, out offset),
                ResourceRecordFactory.GetAllFromArray(message, offset, header.AuthorityRecordCount, out offset),
                ResourceRecordFactory.GetAllFromArray(message, offset, header.AdditionalRecordCount, out offset),
                message);
        }

        public Response(
            Header header, 
            IList<Question> questions, 
            IList<IResourceRecord> answers,
            IList<IResourceRecord> authority, 
            IList<IResourceRecord> additional,
            byte[]? originalResponse = null)
        {
            _header = header;
            _questions = questions;
            _answers = answers;
            _authority = authority;
            _additional = additional;
            _originalResponse = originalResponse;
        }

        public Response()
        {
            _header = new Header();
            _questions = new List<Question>();
            _answers = new List<IResourceRecord>();
            _authority = new List<IResourceRecord>();
            _additional = new List<IResourceRecord>();

            _header.Response = true;
        }

        public Response(IResponse response)
        {
            _header = new Header();
            _questions = new List<Question>(response.Questions);
            _answers = new List<IResourceRecord>(response.AnswerRecords);
            _authority = new List<IResourceRecord>(response.AuthorityRecords);
            _additional = new List<IResourceRecord>(response.AdditionalRecords);

            _header.Response = true;

            Id = response.Id;
            RecursionAvailable = response.RecursionAvailable;
            AuthorativeServer = response.AuthorativeServer;
            OperationCode = response.OperationCode;
            ResponseCode = response.ResponseCode;
        }

        public IList<Question> Questions
        {
            get { return _questions; }
        }

        public IList<IResourceRecord> AnswerRecords
        {
            get { return _answers; }
        }

        public IList<IResourceRecord> AuthorityRecords
        {
            get { return _authority; }
        }

        public IList<IResourceRecord> AdditionalRecords
        {
            get { return _additional; }
        }

        public int Id
        {
            get { return _header.Id; }
            set { _header.Id = value; }
        }

        public bool RecursionAvailable
        {
            get { return _header.RecursionAvailable; }
            set { _header.RecursionAvailable = value; }
        }

        public bool AuthenticData
        {
            get { return _header.AuthenticData; }
            set { _header.AuthenticData = value; }
        }

        public bool CheckingDisabled
        {
            get { return _header.CheckingDisabled; }
            set { _header.CheckingDisabled = value; }
        }

        public bool AuthorativeServer
        {
            get { return _header.AuthorativeServer; }
            set { _header.AuthorativeServer = value; }
        }

        public bool Truncated
        {
            get { return _header.Truncated; }
            set { _header.Truncated = value; }
        }

        public OperationCode OperationCode
        {
            get { return _header.OperationCode; }
            set { _header.OperationCode = value; }
        }

        public ResponseCode ResponseCode
        {
            get { return _header.ResponseCode; }
            set { _header.ResponseCode = value; }
        }

        public int Size
        {
            get
            {
                return _header.Size +
                    _questions.Sum(q => q.Size) +
                    _answers.Sum(a => a.Size) +
                    _authority.Sum(a => a.Size) +
                    _additional.Sum(a => a.Size);
            }
        }

        public byte[] ToArray()
        {
            if (_originalResponse is not null)
            {
                return _originalResponse.ToArray();
            }
            
            UpdateHeader();
            ByteStream result = new ByteStream(Size);

            result
                .Append(_header.ToArray())
                .Append(_questions.Select(q => q.ToArray()))
                .Append(_answers.Select(a => a.ToArray()))
                .Append(_authority.Select(a => a.ToArray()))
                .Append(_additional.Select(a => a.ToArray()));

            return result.ToArray();
        }

        public override string ToString()
        {
            UpdateHeader();

            return ObjectStringifier.New(this)
                .Add(nameof(Header), _header)
                .Add(nameof(Questions), nameof(AnswerRecords), nameof(AuthorityRecords), nameof(AdditionalRecords))
                .ToString();
        }

        private void UpdateHeader()
        {
            _header.QuestionCount = _questions.Count;
            _header.AnswerRecordCount = _answers.Count;
            _header.AuthorityRecordCount = _authority.Count;
            _header.AdditionalRecordCount = _additional.Count;
        }
    }
}
