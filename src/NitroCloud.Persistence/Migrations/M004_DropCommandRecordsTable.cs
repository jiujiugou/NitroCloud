using FluentMigrator;

namespace NitroCloud.Persistence.Migrations;

[Migration(4)]
public sealed class M004_DropCommandRecordsTable : Migration
{
    public override void Up()
    {
        Delete.Table("command_records");
    }

    public override void Down()
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
}