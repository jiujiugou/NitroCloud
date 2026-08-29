using FluentMigrator;

namespace NitroCloud.Persistence.Migrations;

/// <summary>
/// 认证与审计（ADR-015）：users 表（登录账号）+ command_records 加 requested_by（命令发起人审计列）。
/// 角色仅字符串列，roles 表 / RBAC 留演进；存量命令 requested_by 为 null（历史数据不补填）。
/// </summary>
[Migration(6)]
public sealed class M006_CreateUsersTableAndCommandRequestedBy : Migration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("users")
            .WithColumn("id").AsString(64).PrimaryKey()
            .WithColumn("username").AsString(64).NotNullable()
            .WithColumn("display_name").AsString(128).NotNullable()
            .WithColumn("password_hash").AsString(256).NotNullable()
            .WithColumn("role").AsString(32).NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable()
            .WithColumn("created_at").AsString(64).NotNullable();

        Create.Index("idx_users_username")
            .OnTable("users")
            .OnColumn("username").Unique();

        // 命令审计：谁发起的（ADR-015），存量数据为 null
        Alter.Table("command_records")
            .AddColumn("requested_by").AsString(64).Nullable();
    }

    /// <inheritdoc />
    public override void Down()
    {
        Delete.Column("requested_by").FromTable("command_records");
        Delete.Index("idx_users_username").OnTable("users");
        Delete.Table("users");
    }
}
