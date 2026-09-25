using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "category_translations",
                columns: table => new
                {
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category_translations", x => new { x.CategoryId, x.Locale });
                    table.ForeignKey(
                        name: "FK_category_translations_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_translations",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    ShortDescription = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    SeoTitle = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    SeoDescription = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_translations", x => new { x.ProductId, x.Locale });
                    table.ForeignKey(
                        name: "FK_product_translations_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Translated titles are searchable too: 'simple' config (no English stemming) for any language, plus trigrams for typos.
            migrationBuilder.Sql("""
                ALTER TABLE product_translations ADD COLUMN search_title tsvector GENERATED ALWAYS AS (to_tsvector('simple', coalesce("Title", ''))) STORED;
                CREATE INDEX ix_product_translations_search_title ON product_translations USING gin (search_title);
                CREATE INDEX ix_product_translations_title_lower_trgm ON product_translations USING gin (lower("Title") gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "category_translations");

            migrationBuilder.DropTable(
                name: "product_translations");
        }
    }
}
