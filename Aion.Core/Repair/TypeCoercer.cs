using Aion.Core.Interfaces;

namespace Aion.Core.Repair;

public class TypeCoercer
{
    public object Coerce(string value, Type targetType)
    {
        try
        {
            if (targetType == typeof(int)) return int.TryParse(value, out var i) ? i : 0;
            if (targetType == typeof(long)) return long.TryParse(value, out var l) ? l : 0L;
            if (targetType == typeof(double)) return double.TryParse(value, out var d) ? d : 0.0;
            if (targetType == typeof(decimal)) return decimal.TryParse(value, out var m) ? m : 0m;
            if (targetType == typeof(bool)) return bool.TryParse(value, out var b) ? b : false;
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(List<string>)) return new List<string> { value };
            if (targetType == typeof(DateTime)) return DateTime.TryParse(value, out var dt) ? dt : DateTime.MinValue;
        }
        catch { }

        return value;
    }

    public Dictionary<Type, Func<string, object>> GetCoercionMap() => new()
    {
        { typeof(int), s => int.TryParse(s, out var i) ? i : 0 },
        { typeof(long), s => long.TryParse(s, out var l) ? l : 0L },
        { typeof(double), s => double.TryParse(s, out var d) ? d : 0.0 },
        { typeof(decimal), s => decimal.TryParse(s, out var m) ? m : 0m },
        { typeof(bool), s => bool.TryParse(s, out var b) ? b : false },
        { typeof(string), s => s },
        { typeof(List<string>), s => new List<string> { s } },
    };
}
