# NitroCloud.Persistence

SQLite 元数据与业务落库：EF Core `AppDbContext` + FluentMigrator 迁移。

- `Migrations/`：M001~M003 建表迁移（元数据、告警、命令）
- `Sqlite/`：实现 `IAlarmStore` / `ICommandStore`
- `MigrationRunner`：启动时自动执行迁移
