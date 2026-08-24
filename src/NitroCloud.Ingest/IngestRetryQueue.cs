using System.Threading.Channels;
using NitroCloud.Domain.Measurements;
using NitroCloud.Telemetry;

namespace NitroCloud.Ingest;

/// <summary>
/// 写失败重试队列（ADR-008 D6/D5：有上限内存队列，满则丢最旧 + 指标）。
/// 底层用有界 Channel（DropOldest）：容量满时自动丢弃最旧条目，天然防无界膨胀。
/// 线程安全：flush 循环（写方）与 retry drain 循环（读方）可并发使用，无需外部加锁。
/// 重试次数与下次可重试时间由 <see cref="IngestRetryItem"/> 携带，drain 循环按退避调度。
/// </summary>
public sealed class IngestRetryQueue
{
    // 有界通道：队列主体。构造时 SingleReader=true（仅 retry drain 循环读取），SingleWriter=false（多个写方均可入队）
    private readonly Channel<IngestRetryItem> _channel;
    /// <summary>单批最大重试次数（构造时钳制为至少 1）</summary>
    private readonly int _maxAttempts;
    /// <summary>指数退避基数；由调用方保证非负（消费方按 RetryBaseBackoffSeconds 至少 1 秒钳制），本类不钳制</summary>
    private readonly TimeSpan _baseBackoff;

    /// <summary>创建重试队列</summary>
    /// <param name="capacity">队列上限（批次，构造时钳制为至少 1，避免非法容量导致 Channel 构造抛错）</param>
    /// <param name="maxAttempts">单批最大重试次数（钳制为至少 1）</param>
    /// <param name="baseBackoff">指数退避基数（由调用方保证非负）</param>
    public IngestRetryQueue(int capacity, int maxAttempts, TimeSpan baseBackoff)
    {
        _channel = Channel.CreateBounded<IngestRetryItem>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _maxAttempts = Math.Max(1, maxAttempts);
        _baseBackoff = baseBackoff;
    }

    /// <summary>当前队列深度（观测 / 测试用；非精确值，仅作趋势参考）</summary>
    public int Count => _channel.Reader.Count;

    /// <summary>
    /// 入队一个待重试批次（首次失败，重试次数记为 1，下次可重试时间 = 当前）。
    /// 队列满时自动丢弃最旧批次并累加丢弃指标（drop-oldest 降级策略，ADR-008 D5）。
    /// 满则丢最旧的语义由 Channel <see cref="BoundedChannelFullMode.DropOldest"/> 保证。
    /// </summary>
    /// <param name="batch">待重试的测量记录批次</param>
    public void TryEnqueue(IReadOnlyList<MeasurementRecord> batch)
        => TryEnqueue(new IngestRetryItem(batch, 1, DateTime.UtcNow));

    /// <summary>
    /// 把一条 <see cref="IngestRetryItem"/> 放回队列（保留重试次数与下次可重试时间，供 drain 循环未到期放回用）。
    /// 写失败时累加丢弃指标（DropOldest 模式下理论上不会触发，属防御性处理）；写入后刷新队列深度指标。
    /// </summary>
    /// <param name="item">待放回的重试条目（含已重试次数与退避后的下次可重试时间）</param>
    public void TryEnqueue(IngestRetryItem item)
    {
        if (!_channel.Writer.TryWrite(item))
        {
            // DropOldest 模式下理论上不会走到这里，防御性处理
            CloudMetrics.IngestDroppedTotal.Inc();
        }

        CloudMetrics.IngestRetryQueueDepth.Set(Count);
    }

    /// <summary>取出一个条目（队列为空则返回 false，不阻塞；配合 drain 循环的轮询间隔使用）</summary>
    /// <param name="item">取出的重试条目；返回 false 时无意义</param>
    public bool TryDequeue(out IngestRetryItem item) => _channel.Reader.TryRead(out item!);

    /// <summary>
    /// 计算某批次下一次可重试时间（指数退避：base * 2^(attempt-1)，返回 UTC）。
    /// 边界：基数被钳制到最多 1 天、指数位被钳制到 2^20（移位上限），防 <see cref="TimeSpan"/> 溢出；
    /// 第 1 次重试（attempt=1）间隔 = base，之后逐次翻倍。
    /// </summary>
    /// <param name="attempt">下一次将执行的重试序号（从 1 起）</param>
    public DateTime ComputeNextAttemptAt(int attempt)
    {
        var delay = TimeSpan.FromTicks(Math.Min(_baseBackoff.Ticks, TimeSpan.TicksPerDay) * (1L << Math.Min(attempt - 1, 20)));
        return DateTime.UtcNow + delay;
    }

    /// <summary>
    /// 是否已超过最大重试次数（attempt 从 1 计数；attempt &gt; MaxAttempts 即超限，调用方据此丢弃批次）。
    /// </summary>
    /// <param name="attempt">本次的重试序号</param>
    public bool IsExhausted(int attempt) => attempt > _maxAttempts;

    /// <summary>最大重试次数（测试/观测用）</summary>
    public int MaxAttempts => _maxAttempts;
}

/// <summary>
/// 重试队列条目：批次 + 已重试次数 + 下次可重试时间（UTC）。
/// 不可变 record：drain 循环重试失败后以「Attempt+1、新退避时间」重建新条目放回，不改原条目。
/// </summary>
public sealed record IngestRetryItem(IReadOnlyList<MeasurementRecord> Batch, int Attempt, DateTime NextAttemptAt);
