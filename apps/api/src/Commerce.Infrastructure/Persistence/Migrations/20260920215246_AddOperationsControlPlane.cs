using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationsControlPlane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "product_variants",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VariableCostPerUnit",
                table: "product_variants",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "demand_forecasts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    HorizonStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HorizonDays = table.Column<int>(type: "integer", nullable: false),
                    PredictedUnits = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    ConfidenceLower = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    ConfidenceUpper = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    ConfidenceLevel = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: true),
                    Model = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputWindowDays = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Mae = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    Rmse = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    Wape = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    Bias = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demand_forecasts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demand_forecasts_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "demand_observations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ProductViews = table.Column<int>(type: "integer", nullable: false),
                    SearchImpressions = table.Column<int>(type: "integer", nullable: false),
                    AddToCartCount = table.Column<int>(type: "integer", nullable: false),
                    Orders = table.Column<int>(type: "integer", nullable: false),
                    UnitsOrdered = table.Column<int>(type: "integer", nullable: false),
                    UnitsFulfilled = table.Column<int>(type: "integer", nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    EffectivePrice = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    PromotionActive = table.Column<bool>(type: "boolean", nullable: false),
                    StockAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    Channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Region = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demand_observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demand_observations_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operational_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operational_alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "operations_audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operations_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "price_recommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    RecommendedPrice = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    PredictedDemand = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    PredictedRevenue = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    PredictedGrossProfit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    ConfidenceLower = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    ConfidenceUpper = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    ConfidenceLevel = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: true),
                    ReasonCodesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_price_recommendations_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pricing_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    MinimumGrossMarginPercent = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: true),
                    MinimumPrice = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    MaximumPrice = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    MaximumChangePercent = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    MaximumMarkdownPercent = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    ApprovalThresholdPercent = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    EnforceCostFloor = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pricing_policies_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.Id);
                    table.CheckConstraint("ck_promotions_discount", "\"DiscountPercent\" > 0 AND \"DiscountPercent\" <= 100 AND \"EndsAt\" > \"StartsAt\"");
                    table.ForeignKey(
                        name: "FK_promotions_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "price_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CompareAtPrice = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    CostAtTime = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_records", x => x.Id);
                    table.CheckConstraint("ck_price_records_price", "\"Price\" >= 0 AND (\"CompareAtPrice\" IS NULL OR \"CompareAtPrice\" >= \"Price\")");
                    table.ForeignKey(
                        name: "FK_price_records_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_records_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Zone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Bin = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_locations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "replenishment_recommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendedQuantity = table.Column<int>(type: "integer", nullable: false),
                    AverageDailyDemand = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    ReorderPoint = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    ProjectedStockoutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExplanationJson = table.Column<string>(type: "jsonb", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_replenishment_recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_replenishment_recommendations_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_replenishment_recommendations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuantityDelta = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ReferenceId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_ledger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_ledger_inventory_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "inventory_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_ledger_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_ledger_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_stock",
                columns: table => new
                {
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OnHand = table.Column<int>(type: "integer", nullable: false),
                    Reserved = table.Column<int>(type: "integer", nullable: false),
                    SafetyStock = table.Column<int>(type: "integer", nullable: false),
                    Unavailable = table.Column<int>(type: "integer", nullable: false),
                    Inbound = table.Column<int>(type: "integer", nullable: false),
                    SupplierLeadTimeDays = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_stock", x => new { x.WarehouseId, x.VariantId });
                    table.CheckConstraint("ck_warehouse_stock_nonnegative", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"SafetyStock\" >= 0 AND \"Unavailable\" >= 0 AND \"Inbound\" >= 0 AND \"Reserved\" + \"Unavailable\" <= \"OnHand\"");
                    table.ForeignKey(
                        name: "FK_warehouse_stock_inventory_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "inventory_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_stock_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_stock_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_demand_forecasts_VariantId_GeneratedAt",
                table: "demand_forecasts",
                columns: new[] { "VariantId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_demand_observations_VariantId_PeriodStart_Channel_Region",
                table: "demand_observations",
                columns: new[] { "VariantId", "PeriodStart", "Channel", "Region" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_LocationId",
                table: "inventory_ledger",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_VariantId_WarehouseId_CreatedAt",
                table: "inventory_ledger",
                columns: new[] { "VariantId", "WarehouseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_WarehouseId",
                table: "inventory_ledger",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_locations_WarehouseId_Zone_Bin",
                table: "inventory_locations",
                columns: new[] { "WarehouseId", "Zone", "Bin" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operational_alerts_DeduplicationKey_Status",
                table: "operational_alerts",
                columns: new[] { "DeduplicationKey", "Status" },
                unique: true,
                filter: "\"Status\" <> 'Resolved'");

            migrationBuilder.CreateIndex(
                name: "IX_operations_audit_CreatedAt",
                table: "operations_audit",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_operations_audit_ResourceType_ResourceId_CreatedAt",
                table: "operations_audit",
                columns: new[] { "ResourceType", "ResourceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_price_recommendations_Status_GeneratedAt",
                table: "price_recommendations",
                columns: new[] { "Status", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_price_recommendations_VariantId_GeneratedAt",
                table: "price_recommendations",
                columns: new[] { "VariantId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_price_records_PromotionId",
                table: "price_records",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_price_records_Status_EffectiveFrom",
                table: "price_records",
                columns: new[] { "Status", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_price_records_VariantId_EffectiveFrom_EffectiveUntil",
                table: "price_records",
                columns: new[] { "VariantId", "EffectiveFrom", "EffectiveUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_policies_CategoryId_Currency_IsActive",
                table: "pricing_policies",
                columns: new[] { "CategoryId", "Currency", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_promotions_CategoryId_StartsAt_EndsAt",
                table: "promotions",
                columns: new[] { "CategoryId", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_replenishment_recommendations_Status_GeneratedAt",
                table: "replenishment_recommendations",
                columns: new[] { "Status", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_replenishment_recommendations_VariantId",
                table: "replenishment_recommendations",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_replenishment_recommendations_WarehouseId",
                table: "replenishment_recommendations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_stock_LocationId",
                table: "warehouse_stock",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_stock_VariantId_WarehouseId",
                table: "warehouse_stock",
                columns: new[] { "VariantId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_Code",
                table: "warehouses",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demand_forecasts");

            migrationBuilder.DropTable(
                name: "demand_observations");

            migrationBuilder.DropTable(
                name: "inventory_ledger");

            migrationBuilder.DropTable(
                name: "operational_alerts");

            migrationBuilder.DropTable(
                name: "operations_audit");

            migrationBuilder.DropTable(
                name: "price_recommendations");

            migrationBuilder.DropTable(
                name: "price_records");

            migrationBuilder.DropTable(
                name: "pricing_policies");

            migrationBuilder.DropTable(
                name: "replenishment_recommendations");

            migrationBuilder.DropTable(
                name: "warehouse_stock");

            migrationBuilder.DropTable(
                name: "promotions");

            migrationBuilder.DropTable(
                name: "inventory_locations");

            migrationBuilder.DropTable(
                name: "warehouses");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "VariableCostPerUnit",
                table: "product_variants");
        }
    }
}
