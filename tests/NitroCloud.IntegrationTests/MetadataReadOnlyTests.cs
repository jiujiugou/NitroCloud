using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NitroCloud.Api;
using NitroCloud.Api.Controllers;
using NitroCloud.Api.Dtos;
using NitroCloud.Api.Models;
using NitroCloud.Api.Realtime;
using NitroCloud.Persistence;
using NitroCloud.Persistence.Sqlite;
using Xunit;

namespace NitroCloud.IntegrationTests;

/// <summary>
/// 元数据只读约束集成测试（ADR-017）：开关 <c>Metadata:AllowManualCreate=true</c> 时可手动新增
/// 站点/设备/点位（可回退）；改名（PUT）始终可用。默认关闭时的 403 由 UnitTests 覆盖。
/// 建表走 FluentMigrator（snake_case），复用 ADR-012 回归模式。
/// </summary>
public sealed class MetadataReadOnlyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nitrocloud-test-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly MetadataOptions _enabled = new() { AllowManualCreate = true };

    public MetadataReadOnlyTests()
    {
        _connectionString = $"Data Source={_dbPath};Pooling=False";
        MigrationRunner.Run(_connectionString);
        _provider = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseSqlite(_connectionString))
            .BuildServiceProvider();
        _scope = _provider.CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    private static OnlineStatusService NewStatus() =>
        new(
            new InMemoryLatestValueCache(Options.Create(new ApiOptions()), NullLogger<InMemoryLatestValueCache>.Instance),
            Options.Create(new ApiOptions()));

    private static InMemoryLatestValueCache NewCache() =>
        new(Options.Create(new ApiOptions()), NullLogger<InMemoryLatestValueCache>.Instance);

    /// <summary>开关开启时：站点/设备/点位可手动新增，改名（PUT）始终可用。</summary>
    [Fact]
    public async Task AllowManualCreate_enabled_allows_create_and_rename()
    {
        var sites = new SitesController(Db, NewCache(), NewStatus(), Options.Create(_enabled));
        var devices = new DevicesController(Db, NewStatus(), Options.Create(_enabled));
        var points = new PointsController(Db, Options.Create(_enabled));

        // ① 创建站点 → 200，落库
        var siteResult = await sites.Create(new SiteRequestDto { Name = "现场A" });
        var siteDto = Assert.IsType<OkObjectResult>(siteResult.Result);
        Assert.Equal(200, siteDto.StatusCode);
        var site = ((ApiResponse<SiteDto>)siteDto.Value!).Data!;
        Assert.NotNull(site.Id);

        // ② 创建设备 → 200
        var deviceResult = await devices.Create(new DeviceRequestDto { SiteId = site.Id, Name = "PLC-1" });
        var deviceDto = Assert.IsType<OkObjectResult>(deviceResult.Result);
        Assert.Equal(200, deviceDto.StatusCode);
        var device = ((ApiResponse<DeviceDto>)deviceDto.Value!).Data!;

        // ③ 创建点位 → 200
        var pointResult = await points.Create(device.Id, new PointRequestDto { Name = "温度", DataType = "Float" });
        var pointDto = Assert.IsType<OkObjectResult>(pointResult.Result);
        Assert.Equal(200, pointDto.StatusCode);
        var point = ((ApiResponse<PointDto>)pointDto.Value!).Data!;

        // ④ 改名（PUT）→ 200，仍可用
        var renameResult = await sites.Update(site.Id, new SiteRequestDto { Name = "现场A-改名" });
        Assert.Equal(200, Assert.IsType<OkObjectResult>(renameResult.Result).StatusCode);

        // ⑤ 落库校验：三行都在，站点名已改
        var db = Db;
        Assert.NotNull(await db.Sites.FirstOrDefaultAsync(s => s.Id == site.Id && s.Name == "现场A-改名"));
        Assert.NotNull(await db.Devices.FirstOrDefaultAsync(d => d.Id == device.Id));
        Assert.NotNull(await db.Points.FirstOrDefaultAsync(p => p.Id == point.Id));
    }
}
