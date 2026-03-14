using System;
using System.Collections.Generic;
using System.Linq;
using Charon.Dns.Lib.Protocol.EqualityComparers;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol
{
    public class Request : IRequest
    {
        private readonly IList<Question> _questions;
        private Header _header;
        private readonly IList<IResourceRecord> _additional;

        public static Request FromArray(byte[] message)
        {
            Header header = Header.FromArray(message);
            int offset = header.Size;

            if (header.Response || header.QuestionCount == 0 ||
                    header.AnswerRecordCount + header.AuthorityRecordCount > 0 ||
                    header.ResponseCode != ResponseCode.NoError)
            {

                throw new ArgumentException("Invalid request message");
            }

            return new Request(header,
                Question.GetAllFromArray(message, offset, header.QuestionCount, out offset),
                ResourceRecordFactory.GetAllFromArray(message, offset, header.AdditionalRecordCount, out offset));
        }

        public Request(Header header, IList<Question> questions, IList<IResourceRecord> additional)
        {
            this._header = header;
            this._questions = questions;
            this._additional = additional;
        }

        public Request()
        {
            this._questions = new List<Question>();
            this._header = new Header();
            this._additional = new List<IResourceRecord>();

            this._header.OperationCode = OperationCode.Query;
            this._header.Response = false;
            this._header.Id = NextRandomId();
        }

        public Request(IRequest request)
        {
            this._header = new Header();
            this._questions = new List<Question>(request.Questions);
            this._additional = new List<IResourceRecord>(request.AdditionalRecords);

            this._header.Response = false;

            Id = request.Id;
            OperationCode = request.OperationCode;
            RecursionDesired = request.RecursionDesired;
        }

        public IList<Question> Questions
        {
            get { return _questions; }
        }

        public IList<IResourceRecord> AdditionalRecords
        {
            get { return _additional; }
        }

        public int Size
        {
            get
            {
                return _header.Size +
                    _questions.Sum(q => q.Size) +
                    _additional.Sum(a => a.Size);
            }
        }

        public int Id
        {
            get { return _header.Id; }
            set { _header.Id = value; }
        }

        public OperationCode OperationCode
        {
            get { return _header.OperationCode; }
            set { _header.OperationCode = value; }
        }

        public bool RecursionDesired
        {
            get { return _header.RecursionDesired; }
            set { _header.RecursionDesired = value; }
        }

        public byte[] ToArray()
        {
            UpdateHeader();
            ByteStream result = new ByteStream(Size);

            result
                .Append(_header.ToArray())
                .Append(_questions.Select(q => q.ToArray()))
                .Append(_additional.Select(a => a.ToArray()));

            return result.ToArray();
        }

        public override string ToString()
        {
            UpdateHeader();

            return ObjectStringifier.New(this)
                .Add(nameof(Header), _header)
                .Add(nameof(Questions), nameof(AdditionalRecords))
                .ToString();
        }
        
        public override bool Equals(object obj)
        {
            if (obj is null) 
                return false;
            if (ReferenceEquals(this, obj)) 
                return true;
            if (obj is Request request) 
                return Equals(request);
            return false;
        }
        
        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            var headerHash = HeaderComparer.InstanceWithoutIdComparision.GetHashCode(_header);
            hashCode.Add(headerHash);
            foreach (var question in _questions)
            {
                var questionsHash = QuestionComparer.Instance.GetHashCode(question);
                hashCode.Add(questionsHash);
            }

            foreach (var resourceRecord in _additional)
            {
                var resourceRecordHash = ResourceRecordComparer.Instance.GetHashCode(resourceRecord);
                hashCode.Add(resourceRecordHash);
            }

            return hashCode.ToHashCode();
        }
        
        public bool Equals(IRequest other)
        {
            if (other is not Request otherRequest)
            {
                return false;
            }

            if (_questions.Count != otherRequest.Questions.Count)
            {
                return false;
            }

            if (_additional.Count != otherRequest._additional.Count)
            {
                return false;
            }

            if (!HeaderComparer.InstanceWithoutIdComparision.Equals(_header, otherRequest._header))
            {
                return false;
            }
            
            for (var i = 0; i < _questions.Count; i++)
            {
                if (!QuestionComparer.Instance.Equals(_questions[i], otherRequest._questions[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < _additional.Count; i++)
            {
                if (!ResourceRecordComparer.Instance.Equals(_additional[i], otherRequest._additional[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateHeader()
        {
            _header.QuestionCount = _questions.Count;
            _header.AdditionalRecordCount = _additional.Count;
        }

        private ushort NextRandomId()
        {
            var rndId = (ushort)(Random.Shared.Next() % ushort.MaxValue);
            return rndId;
        }
    }
}
