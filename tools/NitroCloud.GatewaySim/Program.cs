using System.Text.Json;
using System.Text.Json.Serialization;
using NitroCloud.Domain.Commands;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Measurements;
using NitroCloud.Shared;
using MqttNet = MQTTnet;

namespace NitroCloud.GatewaySim;

/// <summary>
/// 轻量模拟边缘网关（演示/联调用，DoD 1-5 数据源）。
/// 行为：模拟 N 个现场周期性上行 BatchMeasurements（topic 见 TopicUtil）、偶发告警；
/// 订阅下行 commands，模拟 PLC 写值并回执（含一个写保护点演示 Failed 回执）。
/// 复用云侧 Domain 契约模型 + Shared.TopicUtil，保证 topic/载荷与云侧解析器逐字节一致。
/// 用法：dotnet run --project tools/NitroCloud.GatewaySim
///       [--host localhost] [--port 1883] [--interval 2000] [--sites site-1,site-2] [--client-suffix 1]
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    // ── 固定标识：保证大屏/历史按 deviceId/pointId 关联名称稳定 ──
    private static readonly Guid CncDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid PlcDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");

    /// <summary>写保护演示点（命令契约 pointId 为字符串）：向该点下发写值恒返回 Failed（演示命令失败路径）</summary>
    private const string FailPoint = "b2b2b2b2-0001-4000-8000-0000000000ff";

    private static readonly PointDef[] CncPoints =
    {
        new(Guid.Parse("a1a1a1a1-0001-4000-8000-000000000001"), "主轴温度", DataType.Float, 70.0, 5.0, 0.12),
        new(Guid.Parse("a1a1a1a1-0001-4000-8000-000000000002"), "主轴转速", DataType.Float, 2400.0, 300.0, 0.31),
        new(Guid.Parse("a1a1a1a1-0001-4000-8000-000000000003"), "进给速度", DataType.Float, 180.0, 40.0, 0.53),
    };

    private static readonly PointDef[] PlcPoints =
    {
        new(Guid.Parse("b2b2b2b2-0001-4000-8000-000000000001"), "炉温", DataType.Float, 210.0, 25.0, 0.2),
        new(Guid.Parse("b2b2b2b2-0001-4000-8000-000000000002"), "压力", DataType.Float, 1.6, 0.3, 0.42),
        new(Guid.Parse("b2b2b2b2-0001-4000-8000-000000000003"), "阀门开度", DataType.Float, 45.0, 10.0, 0.61),
        new(Guid.Parse("b2b2b2b2-0001-4000-8000-0000000000ff"), "写保护点-fail", DataType.Float, 0.0, 0.0, 0.0),
    };

    private static readonly SiteDef[] AllSites =
    {
        new("site-1", "华东一号车间", new[] { new DeviceDef(CncDevice, "cnc-01", "数控加工中心", CncPoints) }),
        new("site-2", "华南二号车间", new[] { new DeviceDef(PlcDevice, "plc-01", "注塑机PLC", PlcPoints) }),
    };

    private static async Task<int> Main(string[] args)
    {
        var host = Arg(args, "--host", "localhost");
        var port = int.TryParse(Arg(args, "--port", "1883"), out var p) ? p : 1883;
        var intervalMs = int.TryParse(Arg(args, "--interval", "2000"), out var iv) ? Math.Max(200, iv) : 2000;
        var clientSuffix = Arg(args, "--client-suffix", "1");
        var sitesFilter = Arg(args, "--sites", "site-1,site-2")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();
        var sites = AllSites.Where(s => sitesFilter.Contains(s.Id)).ToArray();

        if (sites.Length == 0)
        {
            Console.Error.WriteLine("[sim] 未匹配到站点，可用: " + string.Join(",", AllSites.Select(s => s.Id)));
            return 2;
        }

        Console.WriteLine($"[sim] 模拟网关启动 broker={host}:{port} interval={intervalMs}ms sites=[{string.Join(",", sites.Select(s => s.Id))}] client-suffix={clientSuffix}");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            await RunAsync(host, port, intervalMs, clientSuffix, sites, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[sim] 已停止");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[sim] 异常退出: {ex}");
            return 1;
        }

        return 0;
    }

    private static async Task RunAsync(string host, int port, int intervalMs, string clientSuffix, SiteDef[] sites, CancellationToken ct)
    {
        var client = new MqttNet.MqttClientFactory().CreateMqttClient();

        // 命令处理器：云 → 网关写值 → 回执（在连接前挂事件）
        client.ApplicationMessageReceivedAsync += e => OnCommandReceivedAsync(e, client);

        // 断线重连（broker 可能后启动）
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var options = new MqttNet.MqttClientOptionsBuilder()
                    .WithTcpServer(host, port)
                    .WithClientId($"nitrocloud-sim-{clientSuffix}")
                    .WithCleanStart()
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                    .Build();

                await client.ConnectAsync(options, ct);
                Console.WriteLine($"[sim] MQTT 已连接 {host}:{port}");

                var subscribe = new MqttNet.MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter("nitrogateway/+/+/commands", MqttNet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();
                await client.SubscribeAsync(subscribe, ct);
                Console.WriteLine("[sim] 已订阅下行命令 nitrogateway/+/+/commands");
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"[sim] 连接失败，3s 后重试: {ex.Message}");
                await Task.Delay(3000, ct);
            }
        }

        // 上行发布主循环
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var t = sw.Elapsed.TotalSeconds;

            foreach (var site in sites)
            {
                foreach (var device in site.Devices)
                {
                    var batch = BuildBatch(site, device, t, now);
                    await PublishJsonAsync(client, TopicUtil.Measurements(site.Id, device.Name), batch, ct);
                }

                await EmitAlarmIfNeededAsync(client, site, t, now, ct);
            }

            await Task.Delay(intervalMs, ct);
        }
    }

    /// <summary>构造一轮测量批次（每条记录含批次去重 id + 站点冗余字段，与云侧解析契约一致）</summary>
    private static BatchMeasurements BuildBatch(SiteDef site, DeviceDef device, double t, DateTime now)
    {
        var records = device.Points.Select(pt =>
        {
            var value = pt.Base + pt.Amp * Math.Sin(t * pt.Phase);
            return new MeasurementRecord
            {
                SiteId = site.Id,
                Id = Guid.NewGuid(),
                DeviceId = device.Id,
                DevicePointId = pt.Id,
                PointName = pt.Name,
                Value = Math.Round(value, 2),
                DataType = pt.DataType,
                Timestamp = now,
                ReceivedAt = now,
                Quality = Quality.Good
            };
        }).ToList();

        return new BatchMeasurements
        {
            SiteId = site.Id,
            V = 1,
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            ScanStartedAt = now,
            ScanCompletedAt = now,
            Records = records
        };
    }

    /// <summary>site-1 温度超限/恢复告警（阈值 73，正弦周期性跨越；alarmId 稳定供云侧幂等 upsert）</summary>
    private static async Task EmitAlarmIfNeededAsync(MqttNet.IMqttClient client, SiteDef site, double t, DateTime now, CancellationToken ct)
    {
        if (site.Id != "site-1")
            return;

        var tempPt = CncPoints[0];
        var value = tempPt.Base + tempPt.Amp * Math.Sin(t * tempPt.Phase);
        const double threshold = 73.0;
        var active = value > threshold;
        const string alarmId = "alm-cnc-temp-001";

        var payload = new
        {
            alarmId,
            ruleId = "rule-temp-high",
            deviceId = CncDevice,
            pointId = tempPt.Id,
            triggerValue = Math.Round(value, 2),
            threshold,
            severity = active ? "Critical" : "Info",
            message = active ? "主轴温度超限" : "主轴温度恢复",
            state = active ? "Active" : "Resolved",
            occurredAt = now
        };

        await PublishJsonAsync(client, TopicUtil.Alarms(site.Id, "cnc-01"), payload, ct);
        Console.WriteLine($"[sim] 告警 {alarmId} state={payload.state} value={payload.triggerValue}");
    }

    /// <summary>下行命令处理：解析 → 模拟 PLC 写值 → 发布回执（写保护点恒失败，演示 Failed 路径）</summary>
    private static async Task OnCommandReceivedAsync(MqttNet.MqttApplicationMessageReceivedEventArgs e, MqttNet.IMqttClient client)
    {
        try
        {
            var parsed = TopicUtil.Parse(e.ApplicationMessage.Topic);
            if (parsed is null || parsed.Value.Kind != TopicKind.Commands)
                return;

            var payload = ReadPayload(e.ApplicationMessage.Payload);
            var request = JsonSerializer.Deserialize<CommandRequest>(payload, JsonOptions);
            if (request is null)
            {
                Console.WriteLine("[sim] 命令载荷解析为空，忽略");
                return;
            }

            var fail = request.PointId.Equals(FailPoint, StringComparison.OrdinalIgnoreCase);
            var ack = new CommandAck
            {
                CommandId = request.CommandId,
                Result = fail ? CommandResult.Failure : CommandResult.Success,
                Error = fail ? "写保护，拒绝写入" : "",
                At = DateTime.UtcNow
            };

            Console.WriteLine($"[sim] 收到命令 commandId={request.CommandId} type={request.Type} pointId={request.PointId} value={request.Value} → {(fail ? "Failure" : "Success")}");

            var topic = TopicUtil.CommandAck(parsed.Value.SiteId, parsed.Value.DeviceId);
            await PublishJsonAsync(client, topic, ack, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[sim] 命令处理异常: {ex.Message}");
        }
    }

    private static async Task PublishJsonAsync(MqttNet.IMqttClient client, string topic, object payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var message = new MqttNet.MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(bytes)
            .WithQualityOfServiceLevel(MqttNet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await client.PublishAsync(message, ct);
    }

    /// <summary>MQTTnet 5.x 载荷为 ReadOnlySequence&lt;byte&gt;，逐段拷贝为字节数组</summary>
    private static byte[] ReadPayload(System.Buffers.ReadOnlySequence<byte> sequence)
    {
        var bytes = new byte[sequence.Length];
        var offset = 0;
        foreach (var segment in sequence)
        {
            segment.Span.CopyTo(bytes.AsSpan(offset));
            offset += segment.Length;
        }

        return bytes;
    }

    private static string Arg(string[] args, string key, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == key)
                return args[i + 1];
        }

        return fallback;
    }

    private sealed record PointDef(Guid Id, string Name, DataType DataType, double Base, double Amp, double Phase);

    private sealed record DeviceDef(Guid Id, string Name, string Model, IReadOnlyList<PointDef> Points);

    private sealed record SiteDef(string Id, string Name, IReadOnlyList<DeviceDef> Devices);
}
