# NitroCloud 中心平台镜像（DoD 6：一条命令拉起）。
# 构建上下文 = 仓库根（docker-compose.yml 指定），根 .dockerignore 已剔除 bin/obj/logs/*.db。
#
# 两阶段：
#   1) build    mcr.microsoft.com/dotnet/sdk:10.0   restore + publish（先 COPY 各 csproj 利用层缓存）
#   2) final    mcr.microsoft.com/dotnet/aspnet:10.0  运行（aspnet 基础镜像默认无 curl，
#                                                        此处 apt 安装 curl 供 compose 健康检查探测 /healthz）
#
# 对外端口 5100（appsettings.json Urls=http://0.0.0.0:5100）；遥测 /metrics 与健康 /healthz 均暴露于此。

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先只拷贝 8 个项目的 .csproj 做 restore：csproj 未变时该层命中缓存，跳过网络 restore 加速重复构建
COPY src/NitroCloud.Domain/NitroCloud.Domain.csproj            src/NitroCloud.Domain/
COPY src/NitroCloud.Shared/NitroCloud.Shared.csproj            src/NitroCloud.Shared/
COPY src/NitroCloud.Storage/NitroCloud.Storage.csproj          src/NitroCloud.Storage/
COPY src/NitroCloud.Telemetry/NitroCloud.Telemetry.csproj      src/NitroCloud.Telemetry/
COPY src/NitroCloud.Persistence/NitroCloud.Persistence.csproj  src/NitroCloud.Persistence/
COPY src/NitroCloud.Influx/NitroCloud.Influx.csproj            src/NitroCloud.Influx/
COPY src/NitroCloud.Ingest/NitroCloud.Ingest.csproj            src/NitroCloud.Ingest/
COPY src/NitroCloud.Api/NitroCloud.Api.csproj                  src/NitroCloud.Api/

RUN dotnet restore src/NitroCloud.Api/NitroCloud.Api.csproj

# 拷贝全部源码并发布（--no-restore 复用上一步 restore 产物）
COPY src/ src/
RUN dotnet publish src/NitroCloud.Api/NitroCloud.Api.csproj \
    -c Release -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# 安装 curl：compose healthcheck（CMD curl -fsS http://localhost:5100/healthz）依赖它
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 5100

ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "NitroCloud.Api.dll"]
