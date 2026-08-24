namespace NitroCloud.Api.Dtos;

/// <summary>
/// 点位 DTO（前端 web/src/api/types.ts 的 Point 对齐）。
/// access 由 Writable 推导：可写 = ReadWrite，只读 = ReadOnly（初版无 WriteOnly）。
/// </summary>
public sealed record PointDto
{
    /// <summary>点位 ID（Guid 字符串）</summary>
    public required string Id { get; init; }

    /// <summary>所属设备</summary>
    public required string DeviceId { get; init; }

    /// <summary>点位名称</summary>
    public required string Name { get; init; }

    /// <summary>数据类型（DataType 枚举名）</summary>
    public required string DataType { get; init; }

    /// <summary>工程单位</summary>
    public string Unit { get; init; } = "";

    /// <summary>是否启用告警</summary>
    public bool AlarmEnabled { get; init; }

    /// <summary>访问权限：ReadOnly/WriteOnly/ReadWrite</summary>
    public required string Access { get; init; }

    /// <summary>点位是否启用（初版恒 true）</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>点位创建/更新请求体</summary>
public sealed record PointRequestDto
{
    /// <summary>点位 ID（可选；创建时不传由服务端生成）</summary>
    public string? Id { get; init; }

    /// <summary>点位名称（必填）</summary>
    public string? Name { get; init; }

    /// <summary>数据类型（DataType 枚举名，必填）</summary>
    public string? DataType { get; init; }

    /// <summary>工程单位（可选）</summary>
    public string? Unit { get; init; }

    /// <summary>是否启用告警</summary>
    public bool? AlarmEnabled { get; init; }

    /// <summary>访问权限（ReadOnly/WriteOnly/ReadWrite，可选）</summary>
    public string? Access { get; init; }
}
