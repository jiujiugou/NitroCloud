using FluentMigrator;

namespace NitroCloud.Persistence.Migrations;

/// <summary>
/// 云端告警汇总表 alarm_records（DESIGN.md §5 领域模型）。
/// 网关 alarmId 幂等 upsert；复合索引 (site_id, state) 支撑按站点/状态过滤。
/// </summary>
[Migration(2)]
public sealed class M002_CreateAlarmRecordsTable : Migration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("alarm_records")
            .WithColumn("id").AsString(64).PrimaryKey()
            .WithColumn("rule_id").AsString(64).NotNullable()
            .WithColumn("site_id").AsString(64).NotNullable()
            .WithColumn("device_id").AsString(64).NotNullable()
            .WithColumn("point_id").AsString(64).NotNullable()
            .WithColumn("trigger_value").AsDouble().NotNullable()
            .WithColumn("threshold").AsDouble().NotNullable()
            .WithColumn("severity").AsString(32).NotNullable()
            .WithColumn("message").AsString(512).NotNullable()
            .WithColumn("state").AsString(32).NotNullable()
            .WithColumn("occurred_at").AsString(64).NotNullable()
            .WithColumn("acked_at").AsString(64).Nullable();

        Create.Index("idx_alarm_records_site_state")
            .OnTable("alarm_records")
            .OnColumn("site_id").Ascending()
            .OnColumn("state").Ascending();
    }

    /// <inheritdoc />
    public override void Down() => Delete.Table("alarm_records");
}
