using FluentMigrator;

namespace NitroCloud.Persistence.Migrations;

/// <summary>
/// 初始元数据表：app_meta（版本记录）、sites、devices、points。
/// 时间列统一存 O 格式 UTC 字符串（字典序即时间序）。
/// </summary>
[Migration(1)]
public sealed class M001_CreateMetadataTables : Migration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("app_meta")
            .WithColumn("key").AsString(64).PrimaryKey()
            .WithColumn("value").AsString(255).Nullable()
            .WithColumn("updated_at").AsString(64).Nullable();

        Create.Table("sites")
            .WithColumn("id").AsString(64).PrimaryKey()
            .WithColumn("name").AsString(128).NotNullable()
            .WithColumn("location").AsString(255).NotNullable()
            .WithColumn("status").AsString(32).NotNullable()
            .WithColumn("created_at").AsString(64).NotNullable();

        Create.Table("devices")
            .WithColumn("id").AsString(64).PrimaryKey()
            .WithColumn("site_id").AsString(64).NotNullable()
            .WithColumn("name").AsString(128).NotNullable()
            .WithColumn("model").AsString(128).NotNullable()
            .WithColumn("last_seen_at").AsString(64).Nullable()
            .WithColumn("created_at").AsString(64).NotNullable();

        Create.Index("idx_devices_site_id")
            .OnTable("devices")
            .OnColumn("site_id").Ascending();

        Create.Table("points")
            .WithColumn("id").AsString(64).PrimaryKey()
            .WithColumn("device_id").AsString(64).NotNullable()
            .WithColumn("name").AsString(128).NotNullable()
            .WithColumn("data_type").AsString(32).NotNullable()
            .WithColumn("unit").AsString(32).NotNullable()
            .WithColumn("alarm_enabled").AsBoolean().NotNullable()
            .WithColumn("writable").AsBoolean().NotNullable();

        Create.Index("idx_points_device_id")
            .OnTable("points")
            .OnColumn("device_id").Ascending();
    }

    /// <inheritdoc />
    public override void Down()
    {
        Delete.Table("points");
        Delete.Table("devices");
        Delete.Table("sites");
        Delete.Table("app_meta");
    }
}
