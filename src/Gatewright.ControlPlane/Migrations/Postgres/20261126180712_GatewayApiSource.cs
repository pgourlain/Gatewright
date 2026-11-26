using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatewright.ControlPlane.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class GatewayApiSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Hosts",
                table: "Apis",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Apis",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceGeneration",
                table: "Apis",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceMissingCount",
                table: "Apis",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceProduct",
                table: "Apis",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRef",
                table: "Apis",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUid",
                table: "Apis",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Apis_Source_SourceRef",
                table: "Apis",
                columns: new[] { "Source", "SourceRef" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Apis_Source_SourceRef",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "Hosts",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "SourceGeneration",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "SourceMissingCount",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "SourceProduct",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "SourceRef",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "SourceUid",
                table: "Apis");
        }
    }
}
