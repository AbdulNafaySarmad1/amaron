using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Generated full-text columns for PostgresProductSearch. They live outside the EF model on purpose: the database
    /// maintains them from Title, Brand and Description, so EF never writes them and the model snapshot is unchanged.
    /// search_title: any-word matching on name and brand. search_all: all-words matching across name, brand and description.
    /// </summary>
    [DbContext(typeof(CommerceDbContext))]
    [Migration("20260925180000_AddProductSearchVectors")]
    public partial class AddProductSearchVectors : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE products ADD COLUMN search_title tsvector GENERATED ALWAYS AS
                    (to_tsvector('english', coalesce("Title", '') || ' ' || coalesce("Brand", ''))) STORED;
                ALTER TABLE products ADD COLUMN search_all tsvector GENERATED ALWAYS AS
                    (setweight(to_tsvector('english', coalesce("Title", '')), 'A')
                     || setweight(to_tsvector('english', coalesce("Brand", '')), 'B')
                     || setweight(to_tsvector('english', coalesce("Description", '')), 'D')) STORED;
                CREATE INDEX ix_products_search_title ON products USING gin (search_title);
                CREATE INDEX ix_products_search_all ON products USING gin (search_all);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_products_search_all;
                DROP INDEX IF EXISTS ix_products_search_title;
                ALTER TABLE products DROP COLUMN IF EXISTS search_all;
                ALTER TABLE products DROP COLUMN IF EXISTS search_title;
                """);
        }
    }
}
