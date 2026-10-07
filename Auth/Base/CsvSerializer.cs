using System.Globalization;
using System.Reflection;

namespace Zuhid.Auth.Base;

public class CsvSerializer
{
    public static List<T> Load<T>(string filePath) where T : class
    {
        var result = new List<T>();
        if (!File.Exists(filePath))
        {
            return result;
        }

        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0)
        {
            return result;
        }

        var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            result.Add(MapRow<T>(lines[i], headers, properties));
        }

        return result;
    }

    private static T MapRow<T>(string line, string[] headers, PropertyInfo[] properties) where T : class
    {
        var values = line.Split(',').Select(v => v.Trim()).ToArray();
        var obj = (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));

        for (var j = 0; j < headers.Length && j < values.Length; j++)
        {
            var property = FindProperty(properties, headers[j]);
            if (property != null && property.CanWrite)
            {
                property.SetValue(obj, ConvertValue(values[j], property.PropertyType));
            }
        }

        return obj;
    }

    private static PropertyInfo? FindProperty(PropertyInfo[] properties, string header) =>
        properties.FirstOrDefault(p =>
            p.Name.Equals(header, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(header.Replace("_", ""), StringComparison.OrdinalIgnoreCase));

    // Type → parser, replacing a large switch so ConvertValue stays low-complexity as more types are added.
    // Types not listed here fall back to Convert.ChangeType.
    private static readonly Dictionary<Type, Func<string, object>> Converters = new()
    {
        [typeof(string)] = raw => raw,
        [typeof(Guid)] = raw => Guid.Parse(raw),
        [typeof(int)] = raw => int.Parse(raw, CultureInfo.InvariantCulture),
        [typeof(long)] = raw => long.Parse(raw, CultureInfo.InvariantCulture),
        [typeof(decimal)] = raw => decimal.Parse(raw, CultureInfo.InvariantCulture),
        [typeof(bool)] = raw => bool.Parse(raw),
        [typeof(DateTime)] = raw => DateTime.SpecifyKind(DateTime.Parse(raw, CultureInfo.InvariantCulture), DateTimeKind.Utc),
        [typeof(DateTimeOffset)] = raw => DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture).ToUniversalTime(),
        [typeof(TimeSpan)] = raw => TimeSpan.Parse(raw, CultureInfo.InvariantCulture),
    };

    private static object? ConvertValue(string rawValue, Type targetType)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return targetType.IsEnum
            ? Enum.Parse(targetType, rawValue)
            : Converters.TryGetValue(targetType, out var converter)
            ? converter(rawValue)
            : Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
    }
}
