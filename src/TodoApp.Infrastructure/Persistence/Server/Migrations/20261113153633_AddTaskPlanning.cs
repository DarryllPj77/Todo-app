using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Infrastructure.Persistence.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Quadrant",
                table: "Tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_Quadrant",
                table: "Tasks",
                sql: "\"Quadrant\" IN (0, 1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_Quadrant",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Quadrant",
                table: "Tasks");
        }
    }
}
