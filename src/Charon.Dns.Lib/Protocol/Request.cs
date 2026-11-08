using System;
using System.Collections.Generic;
using System.Linq;
using Charon.Dns.Lib.Protocol.EqualityComparers;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol;

public class Request : IRequest
{
    private readonly IList<Question> _questions;
    private readonly IList<IResourceRecord> _additional;
    private Header _header;

    public static Request FromArray(byte[] message)
    {
        var header = Header.FromArray(message);
        var offset = header.Size;

        if (header.Response || header.QuestionCount == 0 ||
            header.AnswerRecordCount + header.AuthorityRecordCount > 0 ||
            header.ResponseCode != ResponseCode.NoError)
        {
            throw new ArgumentException("Invalid request message");
        }

        var questions = Question.GetAllFromArray(message, offset, header.QuestionCount, out offset);
        var additional = ResourceRecordFactory.GetAllFromArray(message, offset, header.AdditionalRecordCount);

        return new Request(header, questions, additional);
    }

    public Request(Header header, IList<Question> questions, IList<IResourceRecord> additional)
    {
        _header = header;
        _questions = questions;
        _additional = additional;
    }

    public Request()
    {
        _questions = [];
        _additional = [];
        _header = new Header
        {
            OperationCode = OperationCode.Query,
            Response = false,
            Id = NextRandomId(),
        };
    }

    public Request(IRequest request)
    {
        _questions = [..request.Questions];
        _additional = [..request.AdditionalRecords];

        // A parsed request keeps all header flags (CD, AD), the interface doesn't expose them
        if (request is Request source)
        {
            _header = source._header;
            return;
        }

        _header = new Header
        {
            Response = false,
            Id = request.Id,
            OperationCode = request.OperationCode,
            RecursionDesired = request.RecursionDesired,
        };
    }

    public IList<Question> Questions => _questions;

    public IList<IResourceRecord> AdditionalRecords => _additional;

    IReadOnlyList<Question> IReadOnlyRequest.Questions => (IReadOnlyList<Question>)_questions;
    IReadOnlyList<IResourceRecord> IReadOnlyRequest.AdditionalRecords => (IReadOnlyList<IResourceRecord>)_additional;

    public int Size => _header.Size + _questions.Sum(q => q.Size) + _additional.Sum(a => a.Size);

    public int Id
    {
        get => _header.Id;
        set => _header.Id = value;
    }

    public OperationCode OperationCode
    {
        get => _header.OperationCode;
        set => _header.OperationCode = value;
    }

    public bool RecursionDesired
    {
        get => _header.RecursionDesired;
        set => _header.RecursionDesired = value;
    }

    public byte[] ToArray()
    {
        UpdateHeader();
        var result = new byte[Size];
        WriteToCore(result);
        return result;
    }

    public void WriteTo(Span<byte> destination)
    {
        UpdateHeader();
        WriteToCore(destination);
    }

    private void WriteToCore(Span<byte> destination)
    {
        _header.WriteTo(destination[.._header.Size]);
        var offset = _header.Size;

        offset = WriteEntries(_questions, destination, offset);
        WriteEntries(_additional, destination, offset);
    }

    private static int WriteEntries<T>(IList<T> entries, Span<byte> destination, int offset) where T : IMessageEntry
    {
        foreach (var entry in entries)
        {
            entry.WriteTo(destination.Slice(offset, entry.Size));
            offset += entry.Size;
        }

        return offset;
    }

    public override string ToString()
    {
        UpdateHeader();

        return ObjectStringifier.New(this)
            .Add(nameof(Header), _header)
            .Add(nameof(Questions), nameof(AdditionalRecords))
            .ToString();
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is Request request && Equals(request);
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

    public bool Equals(IRequest? other)
    {
        if (other is not Request otherRequest)
        {
            return false;
        }

        if (_questions.Count != otherRequest._questions.Count)
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

    private static ushort NextRandomId()
    {
        return (ushort)(Random.Shared.Next() % ushort.MaxValue);
    }
}
