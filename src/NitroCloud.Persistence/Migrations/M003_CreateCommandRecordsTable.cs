using FluentMigrator;

namespace NitroCloud.Persistence.Migrations;

/// <summary>
/// 命令记录表 command_records（ADR-008 D6：命令落 SQLite，审计 + 查询 + 状态机）。
/// 幂等键 command_id；复合索引 (site_id, status) 支撑运维查询。
/// </summary>
[Migration(3)]
public sealed class M003_CreateCommandRecordsTable : Migration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("command_records")
            .WithColumn("command_id").AsString(64).PrimaryKey()
            .WithColumn("type").AsString(32).NotNullable()
            .WithColumn("site_id").AsString(64).NotNullable()
            .WithColumn("device_id").AsString(64).NotNullable()
            .WithColumn("point_id").AsString(64).NotNullable()
            .WithColumn("value").AsDouble().NotNullable()
            .WithColumn("requested_at").AsString(64).NotNullable()
            .WithColumn("status").AsString(32).NotNullable()
            .WithColumn("error").AsString(512).Nullable()
            .WithColumn("attempts").AsInt32().NotNullable()
            .WithColumn("sent_at").AsString(64).Nullable()
            .WithColumn("acked_at").AsString(64).Nullable();

        Create.Index("idx_command_records_site_status")
            .OnTable("command_records")
            .OnColumn("site_id").Ascending()
            .OnColumn("status").Ascending();
    }

    /// <inheritdoc />
    public override void Down() => Delete.Table("command_records");
}
