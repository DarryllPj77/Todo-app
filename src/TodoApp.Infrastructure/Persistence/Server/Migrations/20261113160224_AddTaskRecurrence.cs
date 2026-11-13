using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Infrastructure.Persistence.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Recurrence",
                table: "Tasks",
                type: "text",
                nullable: false,
                defaultValueSql: "'{\"Rule\":null,\"Occurrence\":0,\"RuleStart\":0}'");

            migrationBuilder.AddColumn<long>(
                name: "RecurrenceFloor",
                table: "SyncRecords",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SyncRecords_RecurrenceFloor",
                table: "SyncRecords",
                sql: "\"RecurrenceFloor\" BETWEEN 0 AND 1000000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SyncRecords_RecurrenceFloor",
                table: "SyncRecords");

            migrationBuilder.DropColumn(
                name: "Recurrence",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RecurrenceFloor",
                table: "SyncRecords");
        }
    }
}
