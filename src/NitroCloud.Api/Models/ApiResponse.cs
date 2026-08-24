namespace NitroCloud.Api.Models;

/// <summary>
/// 统一 API 响应外壳（沿用网关 ApiResponse 形态，前端 web/src/api/types.ts 的 ApiResponse 对齐）。
/// 所有 REST 端点返回此结构：success + data/error + timestamp。
/// </summary>
public sealed class ApiResponse<T>
{
    /// <summary>是否成功</summary>
    public bool Success { get; init; }

    /// <summary>成功时的业务数据</summary>
    public T? Data { get; init; }

    /// <summary>失败时的错误信息</summary>
    public ApiError? Error { get; init; }

    /// <summary>响应时间（UTC，O 格式）</summary>
    public string Timestamp { get; init; } = DateTime.UtcNow.ToString("O");

    /// <summary>构造成功响应</summary>
    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };

    /// <summary>构造失败响应</summary>
    public static ApiResponse<T> Fail(string code, string message) =>
        new() { Success = false, Error = new ApiError { Code = code, Message = message } };
}

/// <summary>API 错误信息（code 用于前端定位，message 用于展示）</summary>
public sealed class ApiError
{
    /// <summary>错误码</summary>
    public required string Code { get; init; }

    /// <summary>错误消息</summary>
    public required string Message { get; init; }
}
