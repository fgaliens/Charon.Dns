using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol;

public class Response : IResponse
{
    private readonly IList<Question> _questions;
    private readonly IList<IResourceRecord> _answers;
    private readonly IList<IResourceRecord> _authority;
    private readonly IList<IResourceRecord> _additional;
    private Header _header;

    public static Response FromRequest(IRequest request)
    {
        var response = new Response
        {
            Id = request.Id,
            RecursionDesired = request.RecursionDesired,
        };

        foreach (var question in request.Questions)
        {
            response.Questions.Add(question);
        }

        return response;
    }

    public static Response FromArray(byte[] message)
    {
        var header = Header.FromArray(message);
        var offset = header.Size;

        if (!header.Response)
        {
            throw new ArgumentException("Invalid response message");
        }

        var questions = Question.GetAllFromArray(message, offset, header.QuestionCount, out offset);

        if (header.Truncated)
        {
            return new Response(header, questions, [], [], []);
        }

        var answers = ResourceRecordFactory.GetAllFromArray(message, offset, header.AnswerRecordCount, out offset);
        var authority = ResourceRecordFactory.GetAllFromArray(message, offset, header.AuthorityRecordCount, out offset);
        var additional = ResourceRecordFactory.GetAllFromArray(message, offset, header.AdditionalRecordCount);

        return new Response(header, questions, answers, authority, additional);
    }

    public Response(Header header, IList<Question> questions, IList<IResourceRecord> answers,
        IList<IResourceRecord> authority, IList<IResourceRecord> additional)
    {
        _header = header;
        _questions = questions;
        _answers = answers;
        _authority = authority;
        _additional = additional;
    }

    public Response()
    {
        _questions = [];
        _answers = [];
        _authority = [];
        _additional = [];
        _header = new Header
        {
            Response = true,
        };
    }

    public Response(IReadOnlyResponse response)
    {
        _questions = [..response.Questions];
        _answers = [..response.AnswerRecords];
        _authority = [..response.AuthorityRecords];
        _additional = [..response.AdditionalRecords];
        _header = new Header
        {
            Response = true,
            Id = response.Id,
            RecursionDesired = response.RecursionDesired,
            RecursionAvailable = response.RecursionAvailable,
            AuthenticData = response.AuthenticData,
            CheckingDisabled = response.CheckingDisabled,
            AuthorativeServer = response.AuthorativeServer,
            Truncated = response.Truncated,
            OperationCode = response.OperationCode,
            ResponseCode = response.ResponseCode,
        };
    }

    public IList<Question> Questions => _questions;

    public IList<IResourceRecord> AnswerRecords => _answers;

    public IList<IResourceRecord> AuthorityRecords => _authority;

    public IList<IResourceRecord> AdditionalRecords => _additional;

    IReadOnlyList<Question> IReadOnlyResponse.Questions => (IReadOnlyList<Question>)_questions;
    IReadOnlyList<IResourceRecord> IReadOnlyResponse.AnswerRecords => (IReadOnlyList<IResourceRecord>)_answers;
    IReadOnlyList<IResourceRecord> IReadOnlyResponse.AuthorityRecords => (IReadOnlyList<IResourceRecord>)_authority;
    IReadOnlyList<IResourceRecord> IReadOnlyResponse.AdditionalRecords => (IReadOnlyList<IResourceRecord>)_additional;

    public int Id
    {
        get => _header.Id;
        set => _header.Id = value;
    }

    public bool RecursionDesired
    {
        get => _header.RecursionDesired;
        set => _header.RecursionDesired = value;
    }

    public bool RecursionAvailable
    {
        get => _header.RecursionAvailable;
        set => _header.RecursionAvailable = value;
    }

    public bool AuthenticData
    {
        get => _header.AuthenticData;
        set => _header.AuthenticData = value;
    }

    public bool CheckingDisabled
    {
        get => _header.CheckingDisabled;
        set => _header.CheckingDisabled = value;
    }

    public bool AuthorativeServer
    {
        get => _header.AuthorativeServer;
        set => _header.AuthorativeServer = value;
    }

    public bool Truncated
    {
        get => _header.Truncated;
        set => _header.Truncated = value;
    }

    public OperationCode OperationCode
    {
        get => _header.OperationCode;
        set => _header.OperationCode = value;
    }

    public ResponseCode ResponseCode
    {
        get => _header.ResponseCode;
        set => _header.ResponseCode = value;
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

    private int UncompressedSize =>
        _header.Size +
        _questions.Sum(q => q.Size) +
        _answers.Sum(a => a.Size) +
        _authority.Sum(a => a.Size) +
        _additional.Sum(a => a.Size);

    public byte[] ToArray()
    {
        var buffer = new byte[UncompressedSize];
        var size = WriteCompressed(buffer);
        return buffer[..size];
    }

    public void WriteTo(Span<byte> destination)
    {
        WriteCompressed(destination);
    }

    private int WriteCompressed(Span<byte> destination)
    {
        UpdateHeader();

        // Not a using declaration: the writer is passed by reference
        var writer = new MessageWriter(destination);

        try
        {
            writer.WriteHeader(_header);

            foreach (var question in _questions)
            {
                writer.WriteQuestion(question);
            }

            WriteRecords(ref writer, _answers);
            WriteRecords(ref writer, _authority);
            WriteRecords(ref writer, _additional);

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
