# ADR-015：权限管理做「一层认证」（登录态 + 命令下发校验/审计）

- 问题：云端目前无任何认证——REST 管理端点与命令写值裸奔可调。命令写值是云→网关→PLC 的真实物理动作，属安全相关（G1 红线），不能无校验放行。
- 决策背景：用户拍板「先做一层，后端和前端都做上」；大屏保持匿名，管理面板需登录，命令下发必须登录并留审计。

## 可选方案
- A. 不做：面板/命令裸奔 —— 否，命令是物理动作，安全不可接受。
- B. 一层认证（**采用**）：登录态区分「登录/未登录」，管理面板 CRUD 与命令写值加 `[Authorize]`，命令记录发起人；不引入角色/权限点/用户管理页。
- C. 完整 RBAC：用户/角色/权限点/菜单与接口级控制 + 用户管理 —— 超出当前 DoD，留后续演进（users/roles 表已在 DESIGN.md 占位）。

## 决定
- 后端：
  - `POST /api/auth/login`：用户名/密码（PBKDF2 哈希，BCL `Rfc2898DeriveBytes`，不引依赖包）→ 签发 HMAC-SHA256 签名 Token（自研，`base64url(payload).base64url(sig)`，载荷含 userId/username/exp）。
  - `AddAuthentication("Token")` + 自研 `AuthenticationHandler`（共享框架自带，不引 JwtBearer 包）解析 `Authorization: Bearer` → `[Authorize]` 生效。
  - 保护：sites/devices/points/alarms/history/commands 控制器加 `[Authorize]`；登录、大屏只读端点（hub、sites/latest）、healthz、metrics 保持匿名。
  - 审计：`command_records` 加 `requested_by`（发起人），写值落库时记录当前登录用户名。
  - users 表：FluentMigrator M006；默认 admin 引导账号启动时按配置播种（`Auth:AdminUsername/AdminPassword`），密码不落迁移。
- 前端：
  - 登录页 `/login` + 路由守卫（未登录访问 `/admin/*` 跳登录，大屏匿名）。
  - `client.ts` 沿用已预留的 Bearer Token（localStorage）契约，401 统一跳登录。
  - 认证状态用 composable 模块级响应式（不引 Pinia）。
  - 管理面板顶栏加当前用户 + 退出登录。

## 载荷墙（改了会破坏什么）
- `Storage/` 接口只增不删；`Domain/` 不引基础设施；认证代码全部放 `NitroCloud.Api/Auth/`，不动这两处。
- 不新增/升级依赖包（认证用共享框架 + BCL）。
- 大屏必须保持匿名——不得给大屏用的只读端点（hub、sites/latest）加 `[Authorize]`。
- 现有集成测试为 store 层不走 HTTP，不受影响；新增认证单元测试。
- 默认密码 admin/admin123 仅限本地开发，部署必须经环境变量覆盖。

## 验收标准
1. 未登录访问 `/api/commands/write` 与任意管理端点 → 401；登录后 200。
2. 登录成功签发 Token，带 Token 请求管理端点通过；伪造/过期 Token → 401。
3. 命令写值后 `command_records.requested_by` = 发起人用户名。
4. 前端未登录访问 `/admin/*` → 跳 `/login`；登录后进入；退出清理 Token。
5. 大屏匿名可访问（不跳登录）。
6. `dotnet build NitroCloud.slnx` + `dotnet test` 全绿；前端 `npm run build`（vue-tsc）通过。

## 变更记录
- 2026-08-29 新建；用户拍板「先做一层，后端和前端都做上」。
- 2026-08-29 已落地：后端认证 + 前端登录/守卫/401 全通，单测 104 + 集成 5 全绿，`npm run build` 通过。
