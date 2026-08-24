using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NitroCloud.Ingest;

/// <summary>
/// Ingest 模块 DI 扩展（组合根在 Api 项目调用 <see cref="AddNitroIngest"/> 装配）。
/// 注册 MQTT 接入后台宿主服务（<see cref="MqttIngestHostedService"/>，随宿主启停）。
/// 注意：<see cref="ILatestValueCache"/> 与 <see cref="IRealtimeNotifier"/> 的实现由 Api 组合根提供，
/// 本模块只声明对纯接口的依赖，保持模块间解耦（ADR-008 D1）。
/// </summary>
public static class IngestServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Ingest 配置与后台宿主服务，步骤：
    /// ① 绑定 <c>Ingest</c> 配置段到 <see cref="IngestOptions"/>；
    /// ② <c>ValidateOnStart</c>：应用启动时即校验绑定结果（配置段缺失等配置问题启动即暴露，而非运行期才炸）；
    /// ③ 注册 <see cref="MqttIngestHostedService"/> 为宿主托管服务（<c>BackgroundService</c>，StartAsync 启动三循环）。
    /// </summary>
    /// <param name="services">服务容器（组合根传入）</param>
    /// <param name="configuration">配置根，读取其中的 <c>Ingest</c> 段</param>
    public static IServiceCollection AddNitroIngest(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IngestOptions>()
            .Bind(configuration.GetSection("Ingest"))
            .ValidateOnStart();

        services.AddHostedService<MqttIngestHostedService>();
        return services;
    }
}
