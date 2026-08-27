using System.Globalization;
using System.Text.Json;

namespace NitroCloud.Shared;

/// <summary>
/// 测量值类型归一化工具。
/// </summary>
public static class ValueCoercion
{
    /// <summary>
    /// 尝试把载荷值归一为 double。
    /// 
    /// 支持：
    /// - JSON number
    /// - JSON true / false
    /// - JSON string（内容为数字）
    /// - C# double / float / int / long / decimal / bool / string
    /// 
    /// null、数组、对象、无法解析的字符串以及 NaN / Infinity
    /// 均视为不可用。
    /// </summary>
    public static bool TryGetDouble(object? value, out double result)
    {
        result = 0;

        switch (value)
        {
            case null:
                return false;

            // System.Text.Json 反序列化 object 后最常见的情况
            case JsonElement element:
                return TryGetDouble(element, out result);

            case string s:
                return TryParseString(s, out result);

            // C# 原生数值类型（直接来自 .NET 代码/测试，非 JSON 载荷）
            case double d:
                result = d;
                return double.IsFinite(d);

            case float f:
                result = f;
                return double.IsFinite(f);

            case int i:
                result = i;
                return true;

            case long l:
                result = l;
                return true;

            case decimal m:
                result = (double)m;
                return double.IsFinite(result);

            // C# 原生 bool → 1 / 0
            case bool b:
                result = b ? 1 : 0;
                return true;

            default:
                return false;
        }
    }

    private static bool TryGetDouble(
        JsonElement element,
        out double result)
    {
        result = 0;

        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out result)
                       && double.IsFinite(result);

            case JsonValueKind.True:
                result = 1;
                return true;

            case JsonValueKind.False:
                result = 0;
                return true;

            case JsonValueKind.String:
                return TryParseString(
                    element.GetString(),
                    out result);

            default:
                // Null / Array / Object
                return false;
        }
    }

    private static bool TryParseString(
        string? value,
        out double result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        return double.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out result)
            && double.IsFinite(result);
    }
}
