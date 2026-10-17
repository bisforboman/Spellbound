using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spellbound.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMagesAndNews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Mages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    School = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Land = table.Column<int>(type: "INTEGER", nullable: false),
                    Nodes = table.Column<int>(type: "INTEGER", nullable: false),
                    Towns = table.Column<int>(type: "INTEGER", nullable: false),
                    Gold = table.Column<int>(type: "INTEGER", nullable: false),
                    Mana = table.Column<int>(type: "INTEGER", nullable: false),
                    Turns = table.Column<int>(type: "INTEGER", nullable: false),
                    TurnsAccruedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TurnsSpent = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActiveEffect",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpellId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RemainingTurns = table.Column<int>(type: "INTEGER", nullable: false),
                    MageId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveEffect", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActiveEffect_Mages_MageId",
                        column: x => x.MageId,
                        principalTable: "Mages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "News",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_News", x => x.Id);
                    table.ForeignKey(
                        name: "FK_News_Mages_MageId",
                        column: x => x.MageId,
                        principalTable: "Mages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveEffect_MageId",
                table: "ActiveEffect",
                column: "MageId");

            migrationBuilder.CreateIndex(
                name: "IX_Mages_OwnerId",
                table: "Mages",
                column: "OwnerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_News_MageId",
                table: "News",
                column: "MageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveEffect");

            migrationBuilder.DropTable(
                name: "News");

            migrationBuilder.DropTable(
                name: "Mages");
        }
    }
}
