using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NitroCloud.Api;
using NitroCloud.Api.Controllers;
using NitroCloud.Api.Dtos;
using NitroCloud.Api.Realtime;
using NitroCloud.Persistence;
using Xunit;

namespace NitroCloud.UnitTests.Api;

/// <summary>
/// 元数据只读约束单元测试（ADR-017）：默认 <c>Metadata:AllowManualCreate=false</c> 时，
/// Sites/Devices/Points 的 POST（新增）与 DELETE（删除）必须返回 403 MetadataReadOnly，
/// 禁止手动搬运/删除网关元数据；403 分支在查库前返回，故无需真实建库。
/// </summary>
public sealed class MetadataReadOnlyConstraintTests
{
    private static MetadataOptions Disabled() => new(); // 默认 false = 只读

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);

    private static SitesController NewSites(MetadataOptions metadata) =>
        new(
            NewDb(),
            new InMemoryLatestValueCache(Options.Create(new ApiOptions()), NullLogger<InMemoryLatestValueCache>.Instance),
            new OnlineStatusService(
                new InMemoryLatestValueCache(Options.Create(new ApiOptions()), NullLogger<InMemoryLatestValueCache>.Instance),
                Options.Create(new ApiOptions())),
            Options.Create(metadata));

    private static DevicesController NewDevices(MetadataOptions metadata) =>
        new(
            NewDb(),
            new OnlineStatusService(
                new InMemoryLatestValueCache(Options.Create(new ApiOptions()), NullLogger<InMemoryLatestValueCache>.Instance),
                Options.Create(new ApiOptions())),
            Options.Create(metadata));

    private static PointsController NewPoints(MetadataOptions metadata) =>
        new(NewDb(), Options.Create(metadata));

    private static void Assert403(ActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
    }

    [Fact]
    public async Task Sites_create_returns_403_when_manual_create_disabled()
    {
        var ctrl = NewSites(Disabled());
        Assert403((await ctrl.Create(new SiteRequestDto { Name = "手动站点" })).Result!);
    }

    [Fact]
    public async Task Sites_delete_returns_403_when_manual_create_disabled()
    {
        var ctrl = NewSites(Disabled());
        Assert403((await ctrl.Delete("whatever")).Result!);
    }

    [Fact]
    public async Task Devices_create_returns_403_when_manual_create_disabled()
    {
        var ctrl = NewDevices(Disabled());
        Assert403((await ctrl.Create(new DeviceRequestDto { SiteId = "s", Name = "手动设备" })).Result!);
    }

    [Fact]
    public async Task Devices_delete_returns_403_when_manual_create_disabled()
    {
        var ctrl = NewDevices(Disabled());
        Assert403((await ctrl.Delete("whatever")).Result!);
    }

    [Fact]
    public async Task Points_create_returns_403_when_manual_create_disabled()
    {
        var ctrl = NewPoints(Disabled());
        Assert403((await ctrl.Create("deviceId", new PointRequestDto { Name = "手动点位", DataType = "Float" })).Result!);
    }

    [Fact]
    public async Task Points_delete_returns_403_when_manual_create_disabled()
    {
        var ctrl = NewPoints(Disabled());
        Assert403((await ctrl.Delete("deviceId", "pointId")).Result!);
    }
}
