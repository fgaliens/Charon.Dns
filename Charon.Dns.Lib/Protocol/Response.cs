using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol
{
    // TODO: Refactoring: code style + namespace
    public class Response : IResponse
    {
        private Header header;
        private IList<Question> questions;
        private IList<IResourceRecord> answers;
        private IList<IResourceRecord> authority;
        private IList<IResourceRecord> additional;

        public static Response FromRequest(IRequest request)
        {
            Response response = new Response();

            response.Id = request.Id;
            response.RecursionDesired = request.RecursionDesired;

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
                ResourceRecordFactory.GetAllFromArray(message, offset, header.AdditionalRecordCount, out offset));
        }

        public Response(Header header, IList<Question> questions, IList<IResourceRecord> answers,
                IList<IResourceRecord> authority, IList<IResourceRecord> additional)
        {
            this.header = header;
            this.questions = questions;
            this.answers = answers;
            this.authority = authority;
            this.additional = additional;
        }

        public Response()
        {
            this.header = new Header();
            this.questions = new List<Question>();
            this.answers = new List<IResourceRecord>();
            this.authority = new List<IResourceRecord>();
            this.additional = new List<IResourceRecord>();

            this.header.Response = true;
        }

        public Response(IReadOnlyResponse response)
        {
            this.header = new Header();
            this.questions = new List<Question>(response.Questions);
            this.answers = new List<IResourceRecord>(response.AnswerRecords);
            this.authority = new List<IResourceRecord>(response.AuthorityRecords);
            this.additional = new List<IResourceRecord>(response.AdditionalRecords);

            this.header.Response = true;

            Id = response.Id;
            RecursionDesired = response.RecursionDesired;
            RecursionAvailable = response.RecursionAvailable;
            AuthenticData = response.AuthenticData;
            CheckingDisabled = response.CheckingDisabled;
            AuthorativeServer = response.AuthorativeServer;
            Truncated = response.Truncated;
            OperationCode = response.OperationCode;
            ResponseCode = response.ResponseCode;
        }

        public IList<Question> Questions
        {
            get { return questions; }
        }

        public IList<IResourceRecord> AnswerRecords
        {
            get { return answers; }
        }

        public IList<IResourceRecord> AuthorityRecords
        {
            get { return authority; }
        }

        public IList<IResourceRecord> AdditionalRecords
        {
            get { return additional; }
        }

        IReadOnlyList<Question> IReadOnlyResponse.Questions => (IReadOnlyList<Question>)questions;
        IReadOnlyList<IResourceRecord> IReadOnlyResponse.AnswerRecords => (IReadOnlyList<IResourceRecord>)answers;
        IReadOnlyList<IResourceRecord> IReadOnlyResponse.AuthorityRecords => (IReadOnlyList<IResourceRecord>)authority;
        IReadOnlyList<IResourceRecord> IReadOnlyResponse.AdditionalRecords => (IReadOnlyList<IResourceRecord>)additional;

        public int Id
        {
            get { return header.Id; }
            set { header.Id = value; }
        }

        public bool RecursionDesired
        {
            get { return header.RecursionDesired; }
            set { header.RecursionDesired = value; }
        }

        public bool RecursionAvailable
        {
            get { return header.RecursionAvailable; }
            set { header.RecursionAvailable = value; }
        }

        public bool AuthenticData
        {
            get { return header.AuthenticData; }
            set { header.AuthenticData = value; }
        }

        public bool CheckingDisabled
        {
            get { return header.CheckingDisabled; }
            set { header.CheckingDisabled = value; }
        }

        public bool AuthorativeServer
        {
            get { return header.AuthorativeServer; }
            set { header.AuthorativeServer = value; }
        }

        public bool Truncated
        {
            get { return header.Truncated; }
            set { header.Truncated = value; }
        }

        public OperationCode OperationCode
        {
            get { return header.OperationCode; }
            set { header.OperationCode = value; }
        }

        public ResponseCode ResponseCode
        {
            get { return header.ResponseCode; }
            set { header.ResponseCode = value; }
        }

        /// <summary>
        /// Size of the message with compressed domain names.
        /// </summary>
        public int Size
        {
            get
            {
                var uncompressedSize = UncompressedSize;
                var buffer = ArrayPool<byte>.Shared.Rent(uncompressedSize);

                try
                {
                    return WriteCompressed(buffer.AsSpan(0, uncompressedSize));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        private int UncompressedSize
        {
            get
            {
                return header.Size +
                    questions.Sum(q => q.Size) +
                    answers.Sum(a => a.Size) +
                    authority.Sum(a => a.Size) +
                    additional.Sum(a => a.Size);
            }
        }

        public byte[] ToArray()
        {
            var buffer = new byte[UncompressedSize];
            var size = WriteCompressed(buffer);
            Array.Resize(ref buffer, size);
            return buffer;
        }

        public void WriteTo(Span<byte> destination)
        {
            WriteCompressed(destination);
        }

        private int WriteCompressed(Span<byte> destination)
        {
            UpdateHeader();

            var writer = new MessageWriter(destination);

            try
            {
                writer.WriteHeader(header);

                foreach (var question in questions)
                {
                    writer.WriteQuestion(question);
                }

                WriteRecords(ref writer, answers);
                WriteRecords(ref writer, authority);
                WriteRecords(ref writer, additional);

                return writer.Position;
            }
            finally
            {
                writer.Dispose();
            }
        }

        private static void WriteRecords(ref MessageWriter writer, IList<IResourceRecord> records)
        {
            foreach (var record in records)
            {
                writer.WriteRecord(record);
            }
        }

        public override string ToString()
        {
            UpdateHeader();

            return ObjectStringifier.New(this)
                .Add(nameof(Header), header)
                .Add(nameof(Questions), nameof(AnswerRecords), nameof(AuthorityRecords), nameof(AdditionalRecords))
                .ToString();
        }

        private void UpdateHeader()
        {
            header.QuestionCount = questions.Count;
            header.AnswerRecordCount = answers.Count;
            header.AuthorityRecordCount = authority.Count;
            header.AdditionalRecordCount = additional.Count;
        }
    }
}
