using System.Globalization;

namespace NitroCloud.Shared;

/// <summary>
/// 测量值类型归一化工具。
/// 网关载荷的 <c>value</c> 按点位数据类型可能是 number/bool/string（以网关侧契约为准），
/// 而 InfluxDB 的 field 是强类型数值列，面板展示也需要数值，因此统一把可转数值的值归一为 double。
/// 不可转数值（如纯字符串）返回 false，由调用方决定跳过（记指标）或仅作展示。
/// </summary>
public static class ValueCoercion
{
    /// <summary>
    /// 尝试把载荷值归一为 double。
    /// 支持：double/long/int/bool(true=1,false=0) 与可解析的数字字符串；NaN/∞ 视为不可用返回 false。
    /// </summary>
    public static bool TryGetDouble(object? value, out double result)
    {
        result = 0;
        switch (value)
        {
            case null:
                return false;
            case double d:
                result = d;
                break;
            case float f:
                result = f;
                break;
            case int i:
                result = i;
                break;
            case long l:
                result = l;
                break;
            case decimal m:
                result = (double)m;
                break;
            case bool b:
                result = b ? 1 : 0;
                break;
            case string s:
                if (!double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture, out result))
                    return false;
                break;
            default:
                return false;
        }

        return double.IsFinite(result);
    }
}
