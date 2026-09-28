using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using NitroCloud.Ingest;
using NitroCloud.Shared;
using Xunit;

namespace NitroCloud.UnitTests.Ingest;

/// <summary>
/// Ingest MQTT 连接/订阅逻辑单测（<see cref="IngestMqttConnection"/>）：
/// 重点回归「已连接时不得再次 ConnectAsync」——MQTTnet 对已连接客户端的 ConnectAsync 会抛
/// InvalidOperationException("It is not allowed to connect with a server after the connection is established.")，
/// 使「连接成功但订阅失败」后的重试永远无法成功，并让连接泄漏。
/// </summary>
public class IngestMqttConnectionTests
{
    private static readonly IngestOptions Options = new();

    [Fact]
    public async Task ConnectAndSubscribeAsync_WhenAlreadyConnected_SkipsConnectAndSubscribes()
    {
        // 已连接：即便 ConnectAsync 一调用就抛，也应跳过它、只补订阅
        var client = new FakeMqttClient
        {
            IsConnected = true,
            ConnectException = new InvalidOperationException("It is not allowed to connect with a server after the connection is established.")
        };

        var result = await IngestMqttConnection.ConnectAndSubscribeAsync(client, Options, NullLogger.Instance, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, client.ConnectCalls);
        Assert.Equal(1, client.SubscribeCalls);
    }

    [Fact]
    public async Task ConnectAndSubscribeAsync_WhenNotConnected_ConnectsThenSubscribes()
    {
        var client = new FakeMqttClient
        {
            IsConnected = false,
            ConnectResultCode = MqttClientConnectResultCode.Success
        };

        var result = await IngestMqttConnection.ConnectAndSubscribeAsync(client, Options, NullLogger.Instance, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, client.ConnectCalls);
        Assert.Equal(1, client.SubscribeCalls);
    }

    [Fact]
    public async Task ConnectAndSubscribeAsync_WhenConnectResultNotSuccess_ReturnsFailureAndDoesNotSubscribe()
    {
        var client = new FakeMqttClient
        {
            IsConnected = false,
            ConnectResultCode = MqttClientConnectResultCode.NotAuthorized
        };

        var result = await IngestMqttConnection.ConnectAndSubscribeAsync(client, Options, NullLogger.Instance, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Communication, result.Error!.Category);
        Assert.Equal(1, client.ConnectCalls);
        Assert.Equal(0, client.SubscribeCalls);
    }

    [Fact]
    public async Task ConnectAndSubscribeAsync_SubscribeFailureThenRetry_StillConnected_DoesNotReconnect()
    {
        // 首次：连接成功但订阅抛异常——连接仍在
        var client = new FakeMqttClient
        {
            IsConnected = false,
            ConnectResultCode = MqttClientConnectResultCode.Success,
            SubscribeException = new InvalidOperationException("模拟订阅失败")
        };

        var first = await IngestMqttConnection.ConnectAndSubscribeAsync(client, Options, NullLogger.Instance, CancellationToken.None);
        Assert.True(first.IsFailure);

        // 连接未被断开，下一轮（Polly 重试/重连循环）应直接补订阅，不再 ConnectAsync
        client.IsConnected = true;
        client.SubscribeException = null;

        var second = await IngestMqttConnection.ConnectAndSubscribeAsync(client, Options, NullLogger.Instance, CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(1, client.ConnectCalls);   // 未重复连接
        Assert.Equal(2, client.SubscribeCalls);
    }

    /// <summary>仅实现被测路径所需的假客户端：Connect/Subscribe 记录调用次数，IsConnected 可控，其余成员不支持。</summary>
    private sealed class FakeMqttClient : IMqttClient
    {
        public int ConnectCalls { get; private set; }
        public int SubscribeCalls { get; private set; }

        public MqttClientConnectResultCode ConnectResultCode { get; set; } = MqttClientConnectResultCode.Success;
        public Exception? ConnectException { get; set; }
        public Exception? SubscribeException { get; set; }

        public bool IsConnected { get; set; }

        public event Func<MqttApplicationMessageReceivedEventArgs, Task> ApplicationMessageReceivedAsync { add { } remove { } }
        public event Func<MqttClientConnectedEventArgs, Task> ConnectedAsync { add { } remove { } }
        public event Func<MqttClientConnectingEventArgs, Task> ConnectingAsync { add { } remove { } }
        public event Func<MqttClientDisconnectedEventArgs, Task> DisconnectedAsync { add { } remove { } }
        public event Func<InspectMqttPacketEventArgs, Task> InspectPacketAsync { add { } remove { } }

        public MqttClientOptions Options => null!;

        public Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            if (ConnectException is not null)
                throw ConnectException;

            return Task.FromResult(new MqttClientConnectResult { ResultCode = ConnectResultCode });
        }

        public Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken cancellationToken = default)
        {
            SubscribeCalls++;
            if (SubscribeException is not null)
                throw SubscribeException;

            return Task.FromResult<MqttClientSubscribeResult>(null!);
        }

        public void Dispose() { }
        public Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage applicationMessage, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
