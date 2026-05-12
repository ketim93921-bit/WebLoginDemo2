using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebLoginDemo2.Migrations
{
    /// <inheritdoc />
    public partial class AddSensorLogColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Temperature",
                table: "SensorLogs",
                newName: "Temp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Temp",
                table: "SensorLogs",
                newName: "Temperature");
        }
    }
}
