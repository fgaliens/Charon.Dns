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
            var properties = new List<LogEventProperty>();
            properties.Add(new LogEventProperty("Id", new ScalarValue(request.Id)));

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
            
            properties.Add(new LogEventProperty("AdditionalRecordsCount", new ScalarValue(request.AdditionalRecords.Count)));
            
            
            result = new StructureValue(properties);
            return true;
        }
        
        if (value is IResponse response)
        {
            var properties = new List<LogEventProperty>();
            properties.Add(new LogEventProperty("Id", new ScalarValue(response.Id)));

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
            
            properties.Add(new LogEventProperty("AdditionalRecordsCount", new ScalarValue(response.AdditionalRecords.Count)));
            properties.Add(new LogEventProperty("AuthorityRecordsCount", new ScalarValue(response.AuthorityRecords.Count)));
            
            result = new StructureValue(properties);
            return true;
        }
        
        result = null!;
        return false;
    }
}
