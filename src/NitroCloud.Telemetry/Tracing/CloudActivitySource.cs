using System.Diagnostics;

namespace NitroCloud.Telemetry.Tracing;

/// <summary>
/// 云平台 Activity Source（OTel 追踪）。单一 Source，service.name = nitrogateway-cloud（由宿主注入）。
/// </summary>
public static class CloudActivitySource
{
    /// <summary>Activity Source 名称</summary>
    public const string Name = "NitroCloud";

    /// <summary>全局 Activity Source 实例</summary>
    public static readonly ActivitySource Source = new(Name);
}
