using FluentMigrator;

namespace NitroCloud.Persistence.Migrations;

/// <summary>
/// 重建命令记录表 command_records（ADR-010 D2：回写功能落地需要命令落库）。
/// M003 曾建、M004 删（当时 Command 未落地、避免死表）；本次功能落地重建，表结构沿用 M003：
/// command_id PK（幂等键）/ type / site_id / device_id / point_id / value / requested_at / status / error / attempts / sent_at / acked_at，
/// 复合索引 (site_id, status) 支撑运维查询。
/// </summary>
[Migration(5)]
public sealed class M005_RecreateCommandRecordsTable : Migration
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
