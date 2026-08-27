using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NitroCloud.Command;
using NitroCloud.Domain.Alarms;
using NitroCloud.Domain.Commands;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Measurements;
using NitroCloud.Shared;
using NitroCloud.Storage;

namespace NitroCloud.UnitTests.Command;

/// <summary>
/// CommandManager 状态机单测（ADR-010 D3/D5）：
/// Pending → Sent → Acked/Failed/Timeout；幂等（重复回执忽略 / 终态不覆盖 / 未知 commandId 忽略）；
/// 超时重试至上限标 Timeout。
/// </summary>
public class CommandManagerTests
{
    /// <summary>内存命令存储（复刻终态不覆盖语义，供状态断言）</summary>
    private sealed class FakeCommandStore : ICommandStore
    {
        public List<CommandRecord> Records { get; } = new();

        public Task AddAsync(CommandRecord cmd, CancellationToken ct = default)
        {
            Records.Add(cmd);
            return Task.CompletedTask;
        }

        public Task UpdateStatusAsync(
            Guid commandId,
            CommandStatus status,
            string? error = null,
            int? attempts = null,
            DateTime? sentAt = null,
            DateTime? ackedAt = null,
            CancellationToken ct = default)
        {
            var r = Records.First(x => x.CommandId == commandId);
            if (r.IsFinal)
                return Task.CompletedTask; // 终态不覆盖
            r.Status = status;
            r.Error = error;
            if (attempts.HasValue)
                r.Attempts = attempts.Value;
            if (sentAt.HasValue)
                r.SentAt = sentAt.Value;
            if (ackedAt.HasValue)
                r.AckedAt = ackedAt.Value;
            return Task.CompletedTask;
        }

        public Task<CommandRecord?> GetAsync(Guid commandId, CancellationToken ct = default)
            => Task.FromResult(Records.FirstOrDefault(x => x.CommandId == commandId));

        public Task<IReadOnlyList<CommandRecord>> QueryInFlightAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CommandRecord>>(
                Records.Where(r => r.Status is CommandStatus.Pending or CommandStatus.Sent).ToList());
    }

    /// <summary>命令发布 Fake：记录调用，可配置失败</summary>
    private sealed class FakeDispatcher : ICommandDispatcher
    {
        public List<CommandRecord> Dispatched { get; } = new();

        /// <summary>为 true 时发布返回 Communication 失败（模拟 broker 不可用）</summary>
        public bool Fail { get; set; }

        public Task<OperationResult> DispatchAsync(CommandRecord command, CancellationToken ct = default)
        {
            Dispatched.Add(command);
            return Task.FromResult(Fail
                ? (OperationResult)OperationalError.Communication("broker down")
                : OperationResult.Success());
        }
    }

    /// <summary>实时推送 Fake：仅记录回执推送</summary>
    private sealed class FakeNotifier : IRealtimeNotifier
    {
        public List<CommandAck> AckPushes { get; } = new();

        public Task NotifyMeasurementsAsync(string siteId, IReadOnlyList<MeasurementRecord> records, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyAlarmAsync(AlarmRecord alarm, CancellationToken ct = default) => Task.CompletedTask;

        public Task NotifyDeviceStatusAsync(string siteId, DeviceStatus status, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyCommandAckAsync(CommandAck ack, CancellationToken ct = default)
        {
            AckPushes.Add(ack);
            return Task.CompletedTask;
        }
    }

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static readonly Guid AckId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static CommandManager Create(
        FakeCommandStore store,
        FakeDispatcher dispatcher,
        FakeNotifier notifier,
        int timeoutSeconds = 10,
        int maxAttempts = 3)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandStore>(_ => store);
        var provider = services.BuildServiceProvider();

        return new CommandManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            dispatcher,
            notifier,
            Options.Create(new CommandOptions
            {
                TimeoutSeconds = timeoutSeconds,
                MaxAttempts = maxAttempts,
                PollIntervalMs = 1000
            }),
            new CommandAckParser(),
            NullLogger<CommandManager>.Instance);
    }

    private static CommandRecord NewPending(DateTime? requestedAt = null) => new()
    {
        CommandId = AckId,
        SiteId = "site-1",
        DeviceId = "dev-1",
        PointId = "point-1",
        Value = 42,
        RequestedAt = requestedAt ?? DateTime.UtcNow,
        Status = CommandStatus.Pending
    };

    private static string AckJson(string result = "Success", string? error = "", Guid? commandId = null)
        => $$"""
            {"commandId":"{{(commandId ?? AckId)}}","result":"{{result}}","error":"{{error}}","at":"2026-08-27T02:00:00Z"}
            """;

    // ═══════ 回执处理 ═══════

    [Fact]
    public async Task AckSuccess_PendingToAcked_PushesRealtime()
    {
        var store = new FakeCommandStore();
        store.Records.Add(NewPending());
        var dispatcher = new FakeDispatcher();
        var notifier = new FakeNotifier();
        var manager = Create(store, dispatcher, notifier);

        await manager.HandleAckAsync(Utf8(AckJson()));

        var record = store.Records[0];
        Assert.Equal(CommandStatus.Acked, record.Status);
        Assert.NotNull(record.AckedAt);
        Assert.Null(record.Error);
        Assert.Single(notifier.AckPushes);
        Assert.Equal(CommandResult.Success, notifier.AckPushes[0].Result);
        Assert.Empty(dispatcher.Dispatched); // 回执路径不触发发布
    }

    [Fact]
    public async Task AckFailure_PendingToFailed_CarriesError()
    {
        var store = new FakeCommandStore();
        store.Records.Add(NewPending());
        var notifier = new FakeNotifier();
        var manager = Create(store, new FakeDispatcher(), notifier);

        await manager.HandleAckAsync(Utf8(AckJson(result: "Failure", error: "PLC no response")));

        var record = store.Records[0];
        Assert.Equal(CommandStatus.Failed, record.Status);
        Assert.Equal("PLC no response", record.Error);
        Assert.Single(notifier.AckPushes);
        Assert.Equal(CommandResult.Failure, notifier.AckPushes[0].Result);
    }

    [Fact]
    public async Task DuplicateAck_AfterFinal_Ignored_NoSecondPush()
    {
        var store = new FakeCommandStore();
        store.Records.Add(NewPending());
        var notifier = new FakeNotifier();
        var manager = Create(store, new FakeDispatcher(), notifier);

        await manager.HandleAckAsync(Utf8(AckJson()));
        await manager.HandleAckAsync(Utf8(AckJson())); // 重复回执

        Assert.Equal(CommandStatus.Acked, store.Records[0].Status);
        Assert.Single(notifier.AckPushes); // 终态不覆盖：第二次回执未推送
    }

    [Fact]
    public async Task LateAck_AfterTimeout_Ignored()
    {
        var store = new FakeCommandStore();
        var pending = NewPending();
        pending.Status = CommandStatus.Timeout; // 已终态
        store.Records.Add(pending);
        var notifier = new FakeNotifier();
        var manager = Create(store, new FakeDispatcher(), notifier);

        await manager.HandleAckAsync(Utf8(AckJson()));

        Assert.Equal(CommandStatus.Timeout, store.Records[0].Status);
        Assert.Empty(notifier.AckPushes);
    }

    [Fact]
    public async Task UnknownCommandId_Ignored()
    {
        var store = new FakeCommandStore();
        var notifier = new FakeNotifier();
        var manager = Create(store, new FakeDispatcher(), notifier);

        await manager.HandleAckAsync(Utf8(AckJson()));

        Assert.Empty(store.Records);
        Assert.Empty(notifier.AckPushes);
    }

    [Fact]
    public async Task MalformedAck_Ignored_NoException()
    {
        var store = new FakeCommandStore();
        store.Records.Add(NewPending());
        var notifier = new FakeNotifier();
        var manager = Create(store, new FakeDispatcher(), notifier);

        await manager.HandleAckAsync(Utf8("not json"));

        Assert.Equal(CommandStatus.Pending, store.Records[0].Status);
        Assert.Empty(notifier.AckPushes);
    }

    // ═══════ 超时重试扫描 ═══════

    [Fact]
    public async Task Scan_NoTimeout_NoDispatch()
    {
        var store = new FakeCommandStore();
        store.Records.Add(NewPending()); // RequestedAt = now，未超时
        var dispatcher = new FakeDispatcher();
        var manager = Create(store, dispatcher, new FakeNotifier());

        await manager.ScanInFlightAsync();

        Assert.Empty(dispatcher.Dispatched);
        Assert.Equal(CommandStatus.Pending, store.Records[0].Status);
        Assert.Equal(0, store.Records[0].Attempts);
    }

    [Fact]
    public async Task Scan_Timeout_DispatchesAndMarksSent()
    {
        var store = new FakeCommandStore();
        store.Records.Add(NewPending(requestedAt: DateTime.UtcNow.AddSeconds(-11)));
        var dispatcher = new FakeDispatcher();
        var manager = Create(store, dispatcher, new FakeNotifier());

        await manager.ScanInFlightAsync();

        Assert.Single(dispatcher.Dispatched);
        Assert.Equal(CommandStatus.Sent, store.Records[0].Status);
        Assert.Equal(1, store.Records[0].Attempts);
        Assert.NotNull(store.Records[0].SentAt);
    }

    [Fact]
    public async Task Scan_TimeoutRetries3Times_ThenTimeout()
    {
        var store = new FakeCommandStore();
        store.Records.Add(NewPending(requestedAt: DateTime.UtcNow.AddSeconds(-11)));
        var dispatcher = new FakeDispatcher();
        var manager = Create(store, dispatcher, new FakeNotifier(), timeoutSeconds: 10, maxAttempts: 3);

        // 第 1 次：重发（attempts=1 → Sent）
        await manager.ScanInFlightAsync();
        AgeCommand(store, seconds: 11);
        // 第 2 次：重发（attempts=2 → Sent）
        await manager.ScanInFlightAsync();
        AgeCommand(store, seconds: 11);
        // 第 3 次：重发（attempts=3 → Sent）
        await manager.ScanInFlightAsync();
        AgeCommand(store, seconds: 11);
        // 第 4 次：attempts 将达 4 > MaxAttempts(3) → Timeout
        await manager.ScanInFlightAsync();

        Assert.Equal(3, dispatcher.Dispatched.Count); // 恰好重试 3 次
        Assert.Equal(CommandStatus.Timeout, store.Records[0].Status);
        Assert.Equal(3, store.Records[0].Attempts);
    }

    [Fact]
    public async Task Scan_PublishFails_StaysPending_EventuallyTimeout()
    {
        var store = new FakeCommandStore();
        store.Records.Add(NewPending(requestedAt: DateTime.UtcNow.AddSeconds(-11)));
        var dispatcher = new FakeDispatcher { Fail = true };
        var manager = Create(store, dispatcher, new FakeNotifier(), timeoutSeconds: 10, maxAttempts: 3);

        // 发布持续失败：保持 Pending（attempts 累计），达上限标 Timeout
        for (var i = 0; i < 3; i++)
        {
            await manager.ScanInFlightAsync();
        }

        Assert.Equal(CommandStatus.Pending, store.Records[0].Status);
        Assert.Equal(3, dispatcher.Dispatched.Count);

        await manager.ScanInFlightAsync();

        Assert.Equal(CommandStatus.Timeout, store.Records[0].Status);
        Assert.Equal(3, store.Records[0].Attempts);
    }

    /// <summary>把命令的 SentAt 往前拨，模拟时间流逝（触发 Sent 命令的超时判定）</summary>
    private static void AgeCommand(FakeCommandStore store, double seconds)
    {
        store.Records[0].SentAt = DateTime.UtcNow.AddSeconds(-seconds);
    }
}
