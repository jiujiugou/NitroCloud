namespace NitroCloud.Domain.Commands;

/// <summary>
/// 网关命令回执结果（DESIGN.md §4.3 commands/ack 的 result 字段，以网关侧为准）。
/// </summary>
public enum CommandResult
{
    /// <summary>执行成功</summary>
    Success,

    /// <summary>执行失败（error 必填说明原因）</summary>
    Failure
}
