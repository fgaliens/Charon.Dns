using Charon.Dns.Lib.Protocol;
using Serilog.Core;
using Serilog.Events;

namespace Charon.Dns.Logging;

public class LoggingDestructuringPolicies : IDestructuringPolicy
{
    public bool TryDestructure(
        object value, 
        ILogEventPropertyValueFactory propertyValueFactory, 
        out LogEventPropertyValue result)
    {
        if (value is IRequest request)
        {
            var properties = new List<LogEventProperty>
            {
                new LogEventProperty("Type", new ScalarValue("REQUEST")),
                new LogEventProperty("Id", new ScalarValue(request.Id)),
            };

            if (request.Questions.Count == 0)
            {
                properties.Add(new LogEventProperty("Question", new ScalarValue("Empty request")));
            }
            else if (request.Questions.Count == 1)
            {
                properties.Add(new LogEventProperty("Question", new ScalarValue(request.Questions[0])));
            }
            else
            {
                properties.Add(new LogEventProperty("Questions", new SequenceValue(
                    request.Questions.Select(x => new ScalarValue(x)))));
            }

            if (request.AdditionalRecords.Count > 0)
            {
                properties.Add(new LogEventProperty("AdditionalRecords", new SequenceValue(
                    request.AdditionalRecords.Select(x => new ScalarValue(x)))));
            }

            result = new StructureValue(properties);
            return true;
        }
        
        if (value is IResponse response)
        {
            var properties = new List<LogEventProperty>
            {
                new LogEventProperty("Type", new ScalarValue("RESPONSE")),
                new LogEventProperty("Id", new ScalarValue(response.Id)),
            };

            if (response.Questions.Count == 0)
            {
                properties.Add(new LogEventProperty("Question", new ScalarValue("Empty request")));
            }
            else if (response.Questions.Count == 1)
            {
                properties.Add(new LogEventProperty("Question", new ScalarValue(response.Questions[0])));
            }
            else
            {
                properties.Add(new LogEventProperty("Questions", new SequenceValue(
                    response.Questions.Select(x => new ScalarValue(x)))));
            }
            
            if (response.AnswerRecords.Count == 0)
            {
                properties.Add(new LogEventProperty("Answer", new ScalarValue("Nothing")));
            }
            else if (response.AnswerRecords.Count == 1)
            {
                properties.Add(new LogEventProperty("Answer", new ScalarValue(response.AnswerRecords[0])));
            }
            else
            {
                properties.Add(new LogEventProperty("Answers", new SequenceValue(
                    response.AnswerRecords.Select(x => new ScalarValue(x)))));
            }
            
            if (response.AdditionalRecords.Count > 0)
            {
                properties.Add(new LogEventProperty("AdditionalRecords", new SequenceValue(
                    response.AdditionalRecords.Select(x => new ScalarValue(x)))));
            }
            
            properties.Add(new LogEventProperty("AuthorityRecordsCount", new ScalarValue(response.AuthorityRecords.Count)));

            if (response.Truncated)
            { 
                properties.Add(new LogEventProperty("TRUNCATED", new ScalarValue("TRUE")));
            }
            
            result = new StructureValue(properties);
            return true;
        }
        
        result = null!;
        return false;
    }
}
