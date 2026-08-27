# ADR-011: CI/CD 流水线（GitHub Actions + GHCR 镜像发布）

- 日期: 2026-08-27 | 状态: 已拍板（本 ADR 落码即实施） | 用途: 承接 docs/20 §5「CI/CD：.github/workflows/ 落地（build + test + 镜像构建）」，消除「本地能跑、部署机编译环境不一致」风险
- 参考: NitroGateway 已验证的 ADR-010/058 模式（同作者同栈，2026-08 实机跑通后曾归档删除；本 ADR 是其在云侧的落地）
- 范围: `.github/workflows/ci.yml` + `docker-compose.cd.yml` + 两处必要修复（Api/Dockerfile、ValueCoercion）+ README/docs/notes

## 设计
- **单文件 CI/CD**（`ci.yml`，触发统一入口）：
  - 触发: `push: [master]` + `tags: ['v*']` + `pull_request`（PR 只跑 CI，不发布镜像）。
  - job `build-server`（ubuntu-latest）: checkout → setup-dotnet@v4（10.0.x）→ `dotnet restore NitroCloud.slnx` → `dotnet build -c Release --no-restore` → `dotnet test tests/NitroCloud.UnitTests -c Release --no-build`（NuGet 缓存 `~/.nuget/packages`）。
  - job `build-web`（ubuntu-latest）: checkout → setup-node@v4（node 22）→ `cd web && npm ci && npm run build`（vue-tsc 类型检查 + vite build）。
  - job `validate-compose`: `docker compose config -q`（校验 `docker-compose.yml` 单形态，不运行）。
  - job `build-images`（CD）: `if: push && (refs/heads/master || refs/tags/v*)`，`needs: [build-server, build-web, validate-compose]`（测试全绿才发布）；Buildx + `type=gha` 缓存 + `docker/login-action@v3` 用 `secrets.GITHUB_TOKEN`（默认可用，无需额外 PAT）登录 GHCR。
- 镜像与 tag: `center`（`src/NitroCloud.Api/Dockerfile`）+ `web`（`web/Dockerfile`）→ `ghcr.io/jiujiugou/nitrocloud-{center,web}`；master → `latest` + `sha-<7>`，`vX.Y.Z` tag → `vX.Y.Z` + `sha-<7>`（tag 即版本，发布可追溯；回滚 = 部署机 pull 上一版本 tag 或 sha）。
- `docker-compose.cd.yml`（部署机覆盖文件）: 仅覆盖 `image:` + `build: !reset` + `pull_policy: always`（Compose v5.1.1+ 支持 `!reset`，同 NitroGateway 已验证）；broker/influx/端口/卷/环境变量仍由 `docker-compose.yml` 定义；本地开发仍 `docker compose up -d`（构建路径不变）。
- 边界: 不建 deploy.sh（README 声称的 deploy.sh/--profile full/demo/sim 均为文档漂移，docs/20 §5 已记录，不在本 ADR 范围）；不自动 SSH 到服务器（无服务器凭据，发布产物即交付物）；不改任何依赖版本；`Storage/` 纯接口不动。

## 两处必要修复（不修 CI 无法全绿/镜像无法构建）
- `src/NitroCloud.Api/Dockerfile`: restore 阶段只 COPY 了 8 个 csproj，但 Api 已引用 `NitroCloud.Command`（commit 4720387 加入）→ 容器内 restore/publish 会因缺 Command 的 project.assets.json 报 NETSDK1004。修法: restore 阶段补 `COPY src/NitroCloud.Command/NitroCloud.Command.csproj src/NitroCloud.Command/`（8→9 个项目，注释同步）。
- `src/NitroCloud.Shared/ValueCoercion.cs`: `TryGetDouble` 只处理 JsonElement/string，对 C# 原生 double/float/int/long/decimal/bool 走 default 返回 false → 既有 2 个单测失败（NumericTypes_CoerceToDouble / Bool_CoercesToOneOrZero），CI 若跑全量测试必红。修法: switch 补 C# 原生数值类型（IsFinite 过滤）与 bool→1/0 分支；接口签名与 XML 注释不变。

## 验证
- 本地: `dotnet build NitroCloud.slnx -c Release` 0 错误 0 警告；`dotnet test` 全量全绿（含修复后的 ValueCoercion）；`docker compose config -q` EXIT=0；`docker compose -f docker-compose.yml -f docker-compose.cd.yml config -q` EXIT=0 且 center/web 无 build、image 指向 GHCR。
- CI 上（首次 push 后到 GitHub Actions 观察）: PR 仅跑三档 CI；master push / v* tag 额外触发 build-images 推 GHCR。
