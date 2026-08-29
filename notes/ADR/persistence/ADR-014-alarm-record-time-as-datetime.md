# ADR-014：alarm_records 时间字段由 string 改 DateTime（复用全局 ValueConverter）

- 问题：线上 `/api/alarms/summary` 抛 `InvalidOperationException: The LINQ expression '...string.CompareOrdinal(a.OccurredAt, ...)' could not be translated`。`AlarmsController.Summary()` 与 `AlarmStore.QueryAsync()` 用 `string.CompareOrdinal(OccurredAt, ...)` 做时间过滤，EF Core SQLite 翻译器不支持该方法，查询构建期即异常。
- 代码位置：`NitroCloud.Api/Controllers/AlarmsController.cs:56`；`NitroCloud.Persistence/Sqlite/AlarmStore.cs:60/62`；实体 `Entities/AlarmRecordEntity.cs`（OccurredAt/AckedAt 为 string）；`AppDbContext.cs` 已有全局 `ValueConverter<DateTime,string>`。
- 根因：时间列存 string（UTC O 格式串），查询里调用 .NET 方法 CompareOrdinal，SQLite 提供程序无法翻译。

## 可选方案
- A. 保持 string，改 `a.OccurredAt >= "…"`（字符串运算符可翻译，O 格式字典序=时间序）——最小改动，但保留 string 的坏设计（后续任何时间运算仍要手写转换）。
- B. 实体改 `DateTime`/`DateTime?`，复用全局 ValueConverter，查询用 DateTime 比较（**采用**）——列仍 TEXT(64)，无需迁移，现有数据兼容，Domain/Entity 对齐（Domain 层本就是 DateTime）。
- C. `AsEnumerable`/`ToList` 拉全表到内存规避——否，查询必须在数据库端完成。

## 决定
- 采用 B：`AlarmRecordEntity.OccurredAt` → `DateTime`、`AckedAt` → `DateTime?`；`AlarmStore` 删掉手写 `"O"` 串转换（ToEntity/ToDomain/AckAsync）；Summary 与 QueryAsync 用 DateTime 比较（EF 经 Converter 转列比较，生成 `WHERE occurred_at >= @p`）。
- FluentMigrator M002 列定义 `AsString(64)` 不变，无迁移；存量数据为 UTC O 串，Converter from-provider `DateTime.Parse(…, AssumeUniversal).ToUniversalTime()` 兼容（写/读均 UTC，带 Z 后缀）。

## 载荷墙（改了会破坏什么）
- 数据库列类型/Schema、FluentMigrator 迁移、云端数据不动；时序仍只进 InfluxDB（ADR-001）。
- 仅 alarm_records 改类型；`CommandRecordEntity` 的 RequestedAt/SentAt/AckedAt 同款 string 设计，当前仅 `OrderBy` 可翻译不报错，作为后续一致性候选，本次不扩范围。
- 依赖包版本不变；Storage 接口（IAlarmStore）不变。

## 验收标准
1. 集成测试 `AlarmStoreDateTimeQueryTests`（Summary 形态 CountAsync + QueryAsync From/To + DateTime 往返 + AckAsync）红绿对照：修复前 Summary 形态查询抛翻译异常，修复后全绿；
2. `dotnet build NitroCloud.slnx` 0 警告 0 错误；`dotnet test` 全绿（单测 92 + 集成 4）。

## 变更记录
- 2026-08-29 新建；状态：**已落地**（AlarmRecordEntity + AlarmStore + AlarmsController + 集成回归测试，build/test 全绿）。部署：更新 center 镜像重启即生效，无需改库。
