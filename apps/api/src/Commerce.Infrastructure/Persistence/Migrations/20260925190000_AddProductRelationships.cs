using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_relationships",
                columns: table => new
                {
                    SourceProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RelevanceScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_relationships", x => new { x.SourceProductId, x.TargetProductId, x.Type });
                    table.CheckConstraint("ck_product_relationships_not_self", "\"SourceProductId\" <> \"TargetProductId\"");
                    table.ForeignKey(
                        name: "FK_product_relationships_products_SourceProductId",
                        column: x => x.SourceProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_relationships_products_TargetProductId",
                        column: x => x.TargetProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_relationships_SourceProductId_Type_RelevanceScore",
                table: "product_relationships",
                columns: new[] { "SourceProductId", "Type", "RelevanceScore" });

            migrationBuilder.CreateIndex(
                name: "IX_product_relationships_TargetProductId",
                table: "product_relationships",
                column: "TargetProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_relationships");
        }
    }
}
