using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptSplit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIdentities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // SQLite can only add a foreign key along with its column. EF's AddColumn and AddForeignKey would copy
            // every receipt into a rebuilt table instead; this leaves the existing rows as they are, without an owner.
            migrationBuilder.Sql("""
                ALTER TABLE "Receipts" ADD "OwnerId" TEXT NULL
                    CONSTRAINT "FK_Receipts_Users_OwnerId" REFERENCES "Users" ("Id") ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_OwnerId_CreatedAt",
                table: "Receipts",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_Provider_Subject",
                table: "ExternalIdentities",
                columns: new[] { "Provider", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_UserId_Provider",
                table: "ExternalIdentities",
                columns: new[] { "UserId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Receipts_Users_OwnerId",
                table: "Receipts");

            migrationBuilder.DropTable(
                name: "ExternalIdentities");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_OwnerId_CreatedAt",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Receipts");
        }
    }
}
