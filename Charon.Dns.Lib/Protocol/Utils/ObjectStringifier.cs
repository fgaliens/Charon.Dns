using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Charon.Dns.Lib.Protocol.Utils;

public class ObjectStringifier(object obj)
{
    private readonly Dictionary<string, string> _pairs = new();

    public static ObjectStringifier New(object obj)
    {
        return new ObjectStringifier(obj);
    }

    public static string Stringify(object? obj)
    {
        return StringifyObject(obj);
    }

    private static string StringifyObject(object? obj)
    {
        return obj switch
        {
            null => "null",
            string text => text,
            IDictionary dictionary => StringifyDictionary(dictionary),
            IEnumerable enumerable => StringifyList(enumerable),
            _ => obj.ToString() ?? "null",
        };
    }

    private static string StringifyList(IEnumerable enumerable)
    {
        var items = enumerable.Cast<object?>().Select(StringifyObject);
        return $"[{string.Join(", ", items)}]";
    }

    private static string StringifyDictionary(IDictionary dictionary)
    {
        var result = new StringBuilder();

        result.Append('{');

        foreach (DictionaryEntry pair in dictionary)
        {
            result
                .Append(pair.Key)
                .Append('=')
                .Append(StringifyObject(pair.Value))
                .Append(", ");
        }

        if (result.Length > 1)
        {
            result.Remove(result.Length - 2, 2);
        }

        return result.Append('}').ToString();
    }

    public ObjectStringifier Remove(params string[] names)
    {
        foreach (var name in names)
        {
            _pairs.Remove(name);
        }

        return this;
    }

    public ObjectStringifier Add(params string[] names)
    {
        var type = obj.GetType();

        foreach (var name in names)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!;
            var value = property.GetValue(obj);

            _pairs.Add(name, StringifyObject(value));
        }

        return this;
    }

    public ObjectStringifier Add(string name, object? value)
    {
        _pairs.Add(name, StringifyObject(value));
        return this;
    }

    public ObjectStringifier AddAll()
    {
        var properties = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var value = property.GetValue(obj);
            _pairs.Add(property.Name, StringifyObject(value));
        }

        return this;
    }

    public override string ToString()
    {
        return StringifyDictionary(_pairs);
    }
}
