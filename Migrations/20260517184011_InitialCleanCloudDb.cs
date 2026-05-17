using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebLoginDemo2.Migrations
{
    /// <inheritdoc />
    public partial class InitialCleanCloudDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PasswordHash = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SensorHourlyStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AvgTemp = table.Column<double>(type: "double", nullable: false),
                    AvgHumidity = table.Column<double>(type: "double", nullable: false),
                    AvgSoil = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SensorHourlyStats", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SensorLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Temp = table.Column<double>(type: "double", nullable: false),
                    Humidity = table.Column<double>(type: "double", nullable: false),
                    Soil = table.Column<double>(type: "double", nullable: false),
                    SoilState = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TempLimit = table.Column<double>(type: "double", nullable: false),
                    SoilLimit = table.Column<int>(type: "int", nullable: false),
                    TempAuto = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SoilAuto = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Relay1 = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Relay2 = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Relay3 = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Relay4 = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Relay5 = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Relay6 = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Stepper = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SensorLogs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "SensorHourlyStats");

            migrationBuilder.DropTable(
                name: "SensorLogs");
        }
    }
}
