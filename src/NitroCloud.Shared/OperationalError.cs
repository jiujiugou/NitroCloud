namespace NitroCloud.Shared;

/// <summary>
/// 操作错误，携带错误码、消息和可选的附加信息。
/// 用于替代异常在调用链中传递故障信息，避免在采集热路径中抛出异常。
/// 模式沿用 NitroGateway.Shared.OperationalError（ADR-008 D2：Shared 提供 OperationResult<T> 等通用件）。
/// </summary>
public sealed class OperationalError
{
    /// <summary>错误码，如 "Ingest.ParseFailed"、"Storage.WriteFailed"、"Command.Timeout"</summary>
    public required string Code { get; init; }

    /// <summary>人类可读的错误描述</summary>
    public required string Message { get; init; }

    /// <summary>附加信息（如超时毫秒数、失败的批次 ID 等）</summary>
    public Dictionary<string, object>? Details { get; init; }

    /// <summary>错误严重性</summary>
    public OperationalSeverity Severity { get; init; }

    /// <summary>错误分类</summary>
    public ErrorCategory Category { get; init; } = ErrorCategory.General;

    /// <summary>以 "[Code] Message" 形式输出，便于日志检索</summary>
    public override string ToString() => $"[{Code}] {Message}";

    /// <summary>创建一个超时错误（命令回执超时等）</summary>
    public static OperationalError Timeout(string message) => new()
    {
        Category = ErrorCategory.Communication,
        Code = "Timeout",
        Severity = OperationalSeverity.Warning,
        Message = message
    };

    /// <summary>创建一个通信错误（MQTT 连接/发布失败等）</summary>
    public static OperationalError Communication(string message) => new()
    {
        Category = ErrorCategory.Communication,
        Code = "CommunicationError",
        Severity = OperationalSeverity.Warning,
        Message = message
    };

    /// <summary>创建一个解析/协议错误（上行载荷不符合契约等）</summary>
    public static OperationalError Protocol(string message) => new()
    {
        Category = ErrorCategory.Protocol,
        Code = "ProtocolError",
        Severity = OperationalSeverity.Warning,
        Message = message
    };

    /// <summary>创建一个通用错误</summary>
    public static OperationalError General(string message) => new()
    {
        Category = ErrorCategory.General,
        Code = "GeneralError",
        Severity = OperationalSeverity.Warning,
        Message = message
    };

    /// <summary>创建一个参数校验错误</summary>
    public static OperationalError Validation(string message) => new()
    {
        Category = ErrorCategory.Validation,
        Code = "ValidationError",
        Severity = OperationalSeverity.Warning,
        Message = message
    };

    /// <summary>创建一个资源不存在错误</summary>
    public static OperationalError NotFound(string message) => new()
    {
        Category = ErrorCategory.General,
        Code = "NotFound",
        Severity = OperationalSeverity.Info,
        Message = message
    };

    /// <summary>创建一个通用存储错误</summary>
    public static OperationalError Storage(string message) => new()
    {
        Category = ErrorCategory.Storage,
        Code = "StorageError",
        Severity = OperationalSeverity.Error,
        Message = message
    };
}

/// <summary>错误严重性</summary>
public enum OperationalSeverity
{
    /// <summary>信息性错误（不影响主流程）</summary>
    Info,

    /// <summary>警告性错误（可能影响主流程）</summary>
    Warning,

    /// <summary>严重错误（会导致相关流程中断）</summary>
    Error,

    /// <summary>致命错误（会导致宿主崩溃）</summary>
    Critical
}

/// <summary>错误分类</summary>
public enum ErrorCategory
{
    /// <summary>通信（MQTT/网络/超时）</summary>
    Communication,

    /// <summary>存储（SQLite/InfluxDB）</summary>
    Storage,

    /// <summary>协议/载荷解析</summary>
    Protocol,

    /// <summary>参数校验</summary>
    Validation,

    /// <summary>系统资源</summary>
    Resource,

    /// <summary>通用</summary>
    General
}
