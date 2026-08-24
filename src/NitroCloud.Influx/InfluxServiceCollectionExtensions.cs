using InfluxDB.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NitroCloud.Storage;

namespace NitroCloud.Influx;

/// <summary>
/// Influx 模块 DI 扩展（组合根在 Api 调用）。
/// 注册 InfluxDB 客户端 + BatchWriter + ITimeseriesStore 实现。
/// </summary>
public static class InfluxServiceCollectionExtensions
{
    /// <summary>
    /// 注册 InfluxDB 基础设施。配置段 <c>Influx</c>（Token 必填，未配置拒绝启动）。
    /// </summary>
    public static IServiceCollection AddNitroInflux(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InfluxOptions>()
            .Bind(configuration.GetSection("Influx"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Token), "Influx:Token 未配置（InfluxDB 2.x 访问令牌）")
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<InfluxOptions>>().Value;
            return InfluxDBClientFactory.Create(options.Url, options.Token.ToCharArray());
        });
        services.AddSingleton<IWriteApiAsync>(sp => sp.GetRequiredService<InfluxDBClient>().GetWriteApiAsync());
        services.AddSingleton<IQueryApi>(sp => sp.GetRequiredService<InfluxDBClient>().GetQueryApi());
        services.AddSingleton<BatchWriter>();
        services.AddSingleton<ITimeseriesStore, InfluxTimeseriesStore>();

        return services;
    }
}
