using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data-only repair. The original seeder assigned categories round-robin, which put books in
    /// Electronics and cookware in Books. Re-points the 24 seed products (IDs ...0100-...0123) at the
    /// categories CommerceSeeder now names explicitly. No-op on fresh or non-seeded databases.
    /// </summary>
    [DbContext(typeof(CommerceDbContext))]
    [Migration("20260925123000_FixSeedProductCategories")]
    public partial class FixSeedProductCategories : Migration
    {
        // Seed product index -> category (1 Electronics, 2 Home & Kitchen, 3 Books); mirrors CommerceSeeder.SeedCatalog.
        private static readonly int[] Correct = [1, 1, 1, 2, 2, 2, 3, 3, 2, 1, 2, 2, 1, 2, 1, 2, 2, 1, 1, 1, 2, 3, 1, 1];
        private static readonly int[] RoundRobin = [.. Enumerable.Range(0, 24).Select(i => i % 3 + 1)];

        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Reassign(Correct));

        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Reassign(RoundRobin));

        private static string Reassign(int[] categories)
        {
            var cases = string.Join(" ", categories.Select((category, i) => $"WHEN '{Guid(100 + i)}' THEN '{Guid(category)}'::uuid"));
            var ids = string.Join(", ", categories.Select((_, i) => $"'{Guid(100 + i)}'"));
            return $"""
                UPDATE products SET "CategoryId" = CASE "Id"::text {cases} END
                WHERE "Id"::text IN ({ids})
                  AND (SELECT count(*) FROM categories WHERE "Id" IN ('{Guid(1)}', '{Guid(2)}', '{Guid(3)}')) = 3;
                """;
        }

        private static string Guid(int value) => $"00000000-0000-0000-0000-{value:000000000000}";
    }
}
