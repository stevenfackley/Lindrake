using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniSift.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDataSourcePiiFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_pii",
                table: "data_sources",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "pii_flags",
                table: "data_sources",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_pii",
                table: "data_sources");

            migrationBuilder.DropColumn(
                name: "pii_flags",
                table: "data_sources");
        }
    }
}
