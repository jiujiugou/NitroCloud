namespace NitroCloud.Domain.Measurements;

/// <summary>
/// 数据质量标记，遵循 OPC UA 数据质量规范（与 NitroGateway QualityCode 对齐）。
/// quality != Good 的 record 照常入库，但打上质量标签，面板上灰显（DESIGN.md §4.1）。
/// </summary>
public enum Quality
{
    /// <summary>数据正常，可信</summary>
    Good,

    /// <summary>数据来源不确定</summary>
    Uncertain,

    /// <summary>数据异常，不可信（如采集失败、超时）</summary>
    Bad
}
