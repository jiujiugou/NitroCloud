# ADR-012：EF Core 列名统一按 snake_case 映射（对齐 FluentMigrator Schema）

- 问题：线上启动/查询报 `SQLite Error 1: no such column: c.CommandId`。建表走 FluentMigrator（M001-M005，snake_case：command_id/site_id/requested_at…），而 EF Core 默认按 CLR 属性名(PascalCase)生成列名（CommandId/SiteId/…），两者不一致。
- 代码位置：`src/NitroCloud.Persistence/AppDbContext.cs` `OnModelCreating`（从未配置列名）；触发 SQL 见 `Sqlite/CommandStore.cs` `QueryInFlightAsync`；表结构见 `Migrations/M001~M005`。
- 影响面：不只 command_records——sites/devices/points/alarm_records 的复合列（site_id/created_at/occurred_at/rule_id…）同样会崩，只是先撞在命令表（启动即查询 in-flight 命令）。

## 可选方案
- A. EF Core 按属性名全局转 snake_case 列名（**采用**）：`OnModelCreating` 加循环 `property.SetColumnName(ToSnakeCase(property.Name))`，零依赖、不动库。
- B. 逐属性 `HasColumnName("...")`：改动量大、易漏列，仍要覆盖全部 5 张表。
- C. 引入 `EFCore.NamingConventions` 包 + `UseSnakeCaseNamingConvention()`：一行配置但新增依赖，违反「依赖包不升级/降级」。
- D. 改数据库 Schema（列名改回 PascalCase）：需动云端数据或加迁移，与 FluentMigrator 既有 snake_case 约定冲突，否。

## 决定
- 采用 A：在 `OnModelCreating()` 顶部（时间转换循环之后、显式实体配置之前）加全局 snake_case 列名循环 + 私有 `ToSnakeCase(string)`。显式 `HasColumnName` 若后续出现，因在循环之后执行而优先生效。
- 数据库 Schema、云端数据、FluentMigrator 迁移均不动；`__EFMigrationsHistory` 缺失是正常的（建表走 FluentMigrator，用其自身 `VersionInfo` 表）。

## 载荷墙（改了会破坏什么）
- FluentMigrator M001-M005 是 Schema 权威来源；EF 只做读取映射，不承担建表（无 EnsureCreated / EF 迁移）。
- 不删除/不修改云端数据库；时序只进 InfluxDB（ADR-001）。
- 依赖包版本不变；接口/数据模型不变（纯列名映射）。

## 验收标准
1. 回归测试（IntegrationTests，真实 SQLite + FluentMigrator）红绿对照：修复前列名断言与 CommandStore 读写均失败 → 修复后通过；
2. `dotnet build NitroCloud.slnx` 0 警告 0 错误；`dotnet test` 全绿（单测 92 + 集成 1）。

## 变更记录
- 2026-08-29 新建；状态：**已落地**（AppDbContext.cs + 集成回归测试，build/test 全绿）。部署：更新 center 镜像后云端重启即生效，无需改库。
