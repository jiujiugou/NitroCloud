using Microsoft.Extensions.Logging.Abstractions;
using NitroCloud.Ingest;
using NitroCloud.Shared;
using Xunit;

namespace NitroCloud.UnitTests.Ingest;

/// <summary>
/// MQTT 连接重试策略（Polly）单测：验证重试次数、成功短路、取消不重试、失败结果携带 OperationalError。
/// </summary>
public class MqttConnectRetryPolicyTests
{
    private static MqttConnectRetryPolicy CreatePolicy(int maxRetries = 3)
        => new(maxRetries, TimeSpan.FromMilliseconds(1), NullLogger.Instance);

    private static OperationResult Fail() => OperationalError.Communication("连接失败");

    [Fact]
    public async Task ExecuteAsync_RetriesConfiguredTimes_ThenReturnsFailure()
    {
        var policy = CreatePolicy(maxRetries: 3);
        var calls = 0;

        var result = await policy.ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(Fail());
        });

        // 首次尝试 + 3 次重试 = 4 次调用；失败结果携带 Communication 类 OperationalError
        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCategory.Communication, result.Error!.Category);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task ExecuteAsync_ZeroRetries_ExecutesOnce()
    {
        var policy = CreatePolicy(maxRetries: 0);
        var calls = 0;

        var result = await policy.ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(Fail());
        });

        Assert.True(result.IsFailure);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_StopsRetrying_WhenSucceeds()
    {
        var policy = CreatePolicy(maxRetries: 3);
        var calls = 0;

        var result = await policy.ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(calls == 2 ? OperationResult.Success() : Fail());
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringExecution_ThrowsAndNotRetried()
    {
        var policy = CreatePolicy(maxRetries: 3);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var calls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await policy.ExecuteAsync(async token =>
            {
                calls++;
                await Task.Delay(TimeSpan.FromMilliseconds(500), token); // 令牌取消时抛 OperationCanceledException
                return Fail();
            }, cts.Token));

        // 取消不是连接失败，不触发重试
        Assert.Equal(1, calls);
    }

    [Fact]
    public void MaxRetries_ExposesConfiguredValue()
    {
        var policy = CreatePolicy(maxRetries: 5);
        Assert.Equal(5, policy.MaxRetries);
    }
}
