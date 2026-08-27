using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NitroCloud.Storage;

namespace NitroCloud.Command;

/// <summary>
/// Command 模块 DI 扩展（组合根在 Api 项目调用 <see cref="AddNitroCommand"/> 装配，ADR-010 D1/D6）。
/// 注册：Command 配置绑定 + 启动校验、解析器、MQTT 客户端（同时注册 <see cref="ICommandDispatcher"/> 别名指向同一单例）、
/// 状态机核心、后台宿主服务。
/// 注意：<see cref="IRealtimeNotifier"/> 实现由 Api 组合根提供，本模块只声明对纯接口的依赖（ADR-008 D1）。
/// </summary>
public static class CommandServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Command 配置与后台宿主服务，步骤：
    /// ① 绑定 <c>Command</c> 配置段到 <see cref="CommandOptions"/>；
    /// ② <c>ValidateOnStart</c>：应用启动时即校验绑定结果（配置问题启动即暴露）；
    /// ③ 注册解析器 / MQTT 客户端（含 <see cref="ICommandDispatcher"/> 别名，供 Api 控制器注入触发发布）/ 状态机核心；
    /// ④ 注册 <see cref="CommandHostedService"/> 为宿主托管服务（随宿主启停）。
    /// </summary>
    /// <param name="services">服务容器（组合根传入）</param>
    /// <param name="configuration">配置根，读取其中的 <c>Command</c> 段</param>
    public static IServiceCollection AddNitroCommand(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CommandOptions>()
            .Bind(configuration.GetSection("Command"))
            .ValidateOnStart();

        services.AddSingleton<CommandAckParser>();
        services.AddSingleton<MqttCommandClient>();
        // ICommandDispatcher 别名指向同一 MqttCommandClient 单例：Api 控制器与 CommandManager 共用同一发布器
        services.AddSingleton<ICommandDispatcher>(sp => sp.GetRequiredService<MqttCommandClient>());
        services.AddSingleton<CommandManager>();
        services.AddHostedService<CommandHostedService>();
        return services;
    }
}
