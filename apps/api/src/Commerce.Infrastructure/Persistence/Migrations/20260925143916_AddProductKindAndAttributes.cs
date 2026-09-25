using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductKindAndAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "products",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "general");

            migrationBuilder.AddColumn<string>(
                name: "attributes",
                table: "products",
                type: "jsonb",
                nullable: true);

            // Existing rows would otherwise hold NULL, which EF cannot read as a collection; raw inserts get the same default.
            migrationBuilder.Sql("UPDATE products SET attributes = '[]'::jsonb WHERE attributes IS NULL; ALTER TABLE products ALTER COLUMN attributes SET DEFAULT '[]'::jsonb;");

            // Search and suggestions match lower("Title") LIKE '%term%'. A trigram index on that exact expression keeps
            // them indexed as the catalog grows; the existing index on the raw title cannot serve these queries.
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_products_title_lower_trgm ON products USING gin (lower(\"Title\") gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_products_title_lower_trgm;");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "products");

            migrationBuilder.DropColumn(
                name: "attributes",
                table: "products");
        }
    }
}
