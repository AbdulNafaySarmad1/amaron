using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplyChainExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LotId",
                table: "inventory_ledger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "inventory_ledger",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Available");

            migrationBuilder.CreateTable(
                name: "cycle_counts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CountedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReconciledBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cycle_counts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cycle_counts_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_lots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ManufacturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_lots_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FromWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreateIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreateRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DispatchedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DispatchIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReceivedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceiveIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_transfers", x => x.Id);
                    table.CheckConstraint("ck_stock_transfer_warehouses", "\"FromWarehouseId\" <> \"ToWarehouseId\"");
                    table.ForeignKey(
                        name: "FK_stock_transfers_warehouses_FromWarehouseId",
                        column: x => x.FromWarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_transfers_warehouses_ToWarehouseId",
                        column: x => x.ToWarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    TaxIdentifier = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cycle_count_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleCountId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExpectedQuantity = table.Column<int>(type: "integer", nullable: false),
                    CountedQuantity = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cycle_count_lines", x => x.Id);
                    table.CheckConstraint("ck_cycle_count_quantities", "\"ExpectedQuantity\" >= 0 AND (\"CountedQuantity\" IS NULL OR \"CountedQuantity\" >= 0)");
                    table.ForeignKey(
                        name: "FK_cycle_count_lines_cycle_counts_CycleCountId",
                        column: x => x.CycleCountId,
                        principalTable: "cycle_counts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cycle_count_lines_inventory_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "inventory_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cycle_count_lines_inventory_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "inventory_lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cycle_count_lines_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_balances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_balances", x => x.Id);
                    table.CheckConstraint("ck_inventory_balance_nonnegative", "\"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_balances_inventory_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "inventory_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_inventory_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "inventory_lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                INSERT INTO inventory_balances ("Id", "WarehouseId", "LocationId", "VariantId", "LotId", "State", "Quantity", "UpdatedAt")
                SELECT gen_random_uuid(), "WarehouseId", NULL, "VariantId", NULL, 'Available', "OnHand", "UpdatedAt"
                FROM warehouse_stock;
                """);

            migrationBuilder.CreateTable(
                name: "warehouse_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    SourceLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    InventoryState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CompletedQuantity = table.Column<int>(type: "integer", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ReferenceId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AssignedTo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_tasks", x => x.Id);
                    table.CheckConstraint("ck_warehouse_task_quantities", "\"Quantity\" >= 0 AND \"CompletedQuantity\" >= 0 AND \"CompletedQuantity\" <= \"Quantity\" AND \"Priority\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_warehouse_tasks_inventory_locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "inventory_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_tasks_inventory_locations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "inventory_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_tasks_inventory_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "inventory_lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_tasks_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_tasks_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_transfer_lines", x => x.Id);
                    table.CheckConstraint("ck_stock_transfer_line_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_stock_transfer_lines_inventory_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "inventory_lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_transfer_lines_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_transfer_lines_stock_transfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "stock_transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_product_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierSku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: false),
                    MinimumOrderQuantity = table.Column<int>(type: "integer", nullable: false),
                    OrderMultiple = table.Column<int>(type: "integer", nullable: false),
                    ReliabilityPercent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_product_sources", x => x.Id);
                    table.CheckConstraint("ck_supplier_source_values", "\"UnitCost\" >= 0 AND \"LeadTimeDays\" >= 0 AND \"MinimumOrderQuantity\" > 0 AND \"OrderMultiple\" > 0 AND (\"ReliabilityPercent\" IS NULL OR \"ReliabilityPercent\" BETWEEN 0 AND 100)");
                    table.ForeignKey(
                        name: "FK_supplier_product_sources_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_product_sources_supplier_organizations_SupplierOrg~",
                        column: x => x.SupplierOrganizationId,
                        principalTable: "supplier_organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_users",
                columns: table => new
                {
                    SupplierOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_users", x => new { x.SupplierOrganizationId, x.ApplicationUserId });
                    table.ForeignKey(
                        name: "FK_supplier_users_application_users_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "application_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_users_supplier_organizations_SupplierOrganizationId",
                        column: x => x.SupplierOrganizationId,
                        principalTable: "supplier_organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_invoice_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FiscalInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierSku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_invoice_lines", x => x.Id);
                    table.CheckConstraint("ck_invoice_line_values", "\"Quantity\" > 0 AND \"UnitCost\" >= 0");
                    table.ForeignKey(
                        name: "FK_fiscal_invoice_lines_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PayloadHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    DocumentReference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_invoices_supplier_organizations_SupplierOrganization~",
                        column: x => x.SupplierOrganizationId,
                        principalTable: "supplier_organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedQuantity = table.Column<int>(type: "integer", nullable: false),
                    PhysicalQuantity = table.Column<int>(type: "integer", nullable: false),
                    AcceptedQuantity = table.Column<int>(type: "integer", nullable: false),
                    DamagedQuantity = table.Column<int>(type: "integer", nullable: false),
                    QuarantinedQuantity = table.Column<int>(type: "integer", nullable: false),
                    MissingQuantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goods_receipt_lines", x => x.Id);
                    table.CheckConstraint("ck_receipt_line_quantities", "\"ExpectedQuantity\" > 0 AND \"PhysicalQuantity\" >= 0 AND \"AcceptedQuantity\" >= 0 AND \"DamagedQuantity\" >= 0 AND \"QuarantinedQuantity\" >= 0 AND \"MissingQuantity\" >= 0 AND \"PhysicalQuantity\" = \"AcceptedQuantity\" + \"DamagedQuantity\" + \"QuarantinedQuantity\" AND \"MissingQuantity\" = GREATEST(\"ExpectedQuantity\" - \"PhysicalQuantity\", 0)");
                    table.ForeignKey(
                        name: "FK_goods_receipt_lines_inventory_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "inventory_lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_goods_receipt_lines_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    InboundShipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReceivedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goods_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_goods_receipts_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbound_shipment_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InboundShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedQuantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_shipment_lines", x => x.Id);
                    table.CheckConstraint("ck_inbound_line_quantity", "\"ExpectedQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_inbound_shipment_lines_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbound_shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SupplierOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Carrier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    EstimatedArrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inbound_shipments_supplier_organizations_SupplierOrganizati~",
                        column: x => x.SupplierOrganizationId,
                        principalTable: "supplier_organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FiscalInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExceptionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_matches_fiscal_invoices_FiscalInvoiceId",
                        column: x => x.FiscalInvoiceId,
                        principalTable: "fiscal_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceQuotationLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierSku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OrderedQuantity = table.Column<int>(type: "integer", nullable: false),
                    ReceivedQuantity = table.Column<int>(type: "integer", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: false),
                    MinimumOrderQuantity = table.Column<int>(type: "integer", nullable: false),
                    OrderMultiple = table.Column<int>(type: "integer", nullable: false),
                    ReliabilityPercent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_lines", x => x.Id);
                    table.CheckConstraint("ck_purchase_order_line_quantity", "\"OrderedQuantity\" > 0 AND \"ReceivedQuantity\" >= 0 AND \"ReceivedQuantity\" <= \"OrderedQuantity\" AND \"UnitCost\" >= 0 AND \"LeadTimeDays\" >= 0 AND \"MinimumOrderQuantity\" > 0 AND \"OrderMultiple\" > 0 AND (\"ReliabilityPercent\" IS NULL OR \"ReliabilityPercent\" BETWEEN 0 AND 100)");
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SupplierOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceQuotationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ExpectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_orders_supplier_organizations_SupplierOrganization~",
                        column: x => x.SupplierOrganizationId,
                        principalTable: "supplier_organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_orders_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "request_for_quotation_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestForQuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_for_quotation_lines", x => x.Id);
                    table.UniqueConstraint("AK_request_for_quotation_lines_Id_RequestForQuotationId", x => new { x.Id, x.RequestForQuotationId });
                    table.CheckConstraint("ck_rfq_line_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_request_for_quotation_lines_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "request_for_quotation_suppliers",
                columns: table => new
                {
                    RequestForQuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_for_quotation_suppliers", x => new { x.RequestForQuotationId, x.SupplierOrganizationId });
                    table.ForeignKey(
                        name: "FK_request_for_quotation_suppliers_supplier_organizations_Supp~",
                        column: x => x.SupplierOrganizationId,
                        principalTable: "supplier_organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requests_for_quotation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ResponseDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AwardedQuotationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwardReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AwardedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requests_for_quotation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_requests_for_quotation_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_quotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestForQuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SubmittedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_quotations", x => x.Id);
                    table.UniqueConstraint("AK_supplier_quotations_Id_RequestForQuotationId", x => new { x.Id, x.RequestForQuotationId });
                    table.UniqueConstraint("AK_supplier_quotations_Id_SupplierOrganizationId", x => new { x.Id, x.SupplierOrganizationId });
                    table.ForeignKey(
                        name: "FK_supplier_quotations_requests_for_quotation_RequestForQuotat~",
                        column: x => x.RequestForQuotationId,
                        principalTable: "requests_for_quotation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_quotations_supplier_organizations_SupplierOrganiza~",
                        column: x => x.SupplierOrganizationId,
                        principalTable: "supplier_organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_quotation_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierQuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestForQuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestForQuotationLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierSku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: false),
                    MinimumOrderQuantity = table.Column<int>(type: "integer", nullable: false),
                    OrderMultiple = table.Column<int>(type: "integer", nullable: false),
                    ReliabilityPercent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_quotation_lines", x => x.Id);
                    table.CheckConstraint("ck_quotation_line_values", "\"UnitCost\" >= 0 AND \"LeadTimeDays\" >= 0 AND \"MinimumOrderQuantity\" > 0 AND \"OrderMultiple\" > 0 AND (\"ReliabilityPercent\" IS NULL OR \"ReliabilityPercent\" BETWEEN 0 AND 100)");
                    table.ForeignKey(
                        name: "FK_supplier_quotation_lines_request_for_quotation_lines_Reques~",
                        columns: x => new { x.RequestForQuotationLineId, x.RequestForQuotationId },
                        principalTable: "request_for_quotation_lines",
                        principalColumns: new[] { "Id", "RequestForQuotationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_quotation_lines_supplier_quotations_SupplierQuotat~",
                        columns: x => new { x.SupplierQuotationId, x.RequestForQuotationId },
                        principalTable: "supplier_quotations",
                        principalColumns: new[] { "Id", "RequestForQuotationId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_LotId",
                table: "inventory_ledger",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_lines_CycleCountId_VariantId_LocationId_LotId_S~",
                table: "cycle_count_lines",
                columns: new[] { "CycleCountId", "VariantId", "LocationId", "LotId", "State" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_lines_LocationId",
                table: "cycle_count_lines",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_lines_LotId",
                table: "cycle_count_lines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_lines_VariantId",
                table: "cycle_count_lines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_counts_Number",
                table: "cycle_counts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cycle_counts_WarehouseId_Status_CreatedAt",
                table: "cycle_counts",
                columns: new[] { "WarehouseId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoice_lines_FiscalInvoiceId_PurchaseOrderLineId",
                table: "fiscal_invoice_lines",
                columns: new[] { "FiscalInvoiceId", "PurchaseOrderLineId" },
                unique: true,
                filter: "\"PurchaseOrderLineId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoice_lines_PurchaseOrderLineId",
                table: "fiscal_invoice_lines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoice_lines_VariantId",
                table: "fiscal_invoice_lines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_PurchaseOrderId",
                table: "fiscal_invoices",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_SupplierOrganizationId_PayloadHash",
                table: "fiscal_invoices",
                columns: new[] { "SupplierOrganizationId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_SupplierOrganizationId_Provider_ExternalNum~",
                table: "fiscal_invoices",
                columns: new[] { "SupplierOrganizationId", "Provider", "ExternalNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_lines_GoodsReceiptId_PurchaseOrderLineId",
                table: "goods_receipt_lines",
                columns: new[] { "GoodsReceiptId", "PurchaseOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_lines_LotId",
                table: "goods_receipt_lines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_lines_PurchaseOrderLineId",
                table: "goods_receipt_lines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_lines_VariantId",
                table: "goods_receipt_lines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_IdempotencyKey",
                table: "goods_receipts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_InboundShipmentId",
                table: "goods_receipts",
                column: "InboundShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_Number",
                table: "goods_receipts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_PurchaseOrderId",
                table: "goods_receipts",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_WarehouseId",
                table: "goods_receipts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_shipment_lines_InboundShipmentId_PurchaseOrderLineId",
                table: "inbound_shipment_lines",
                columns: new[] { "InboundShipmentId", "PurchaseOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_shipment_lines_PurchaseOrderLineId",
                table: "inbound_shipment_lines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_shipment_lines_VariantId",
                table: "inbound_shipment_lines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_shipments_Number",
                table: "inbound_shipments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_shipments_PurchaseOrderId",
                table: "inbound_shipments",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_shipments_SupplierOrganizationId_IdempotencyKey",
                table: "inbound_shipments",
                columns: new[] { "SupplierOrganizationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_shipments_SupplierOrganizationId_Status",
                table: "inbound_shipments",
                columns: new[] { "SupplierOrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_LocationId",
                table: "inventory_balances",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_LotId",
                table: "inventory_balances",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_VariantId",
                table: "inventory_balances",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_WarehouseId_LocationId_VariantId_LotId_S~",
                table: "inventory_balances",
                columns: new[] { "WarehouseId", "LocationId", "VariantId", "LotId", "State" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_lots_ExpiresAt",
                table: "inventory_lots",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_lots_VariantId_LotNumber",
                table: "inventory_lots",
                columns: new[] { "VariantId", "LotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_matches_FiscalInvoiceId",
                table: "invoice_matches",
                column: "FiscalInvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_matches_PurchaseOrderId",
                table: "invoice_matches",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_PurchaseOrderId_VariantId",
                table: "purchase_order_lines",
                columns: new[] { "PurchaseOrderId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_SourceQuotationLineId",
                table: "purchase_order_lines",
                column: "SourceQuotationLineId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_VariantId",
                table: "purchase_order_lines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_Number",
                table: "purchase_orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_SourceQuotationId_SupplierOrganizationId",
                table: "purchase_orders",
                columns: new[] { "SourceQuotationId", "SupplierOrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_SupplierOrganizationId_Status_CreatedAt",
                table: "purchase_orders",
                columns: new[] { "SupplierOrganizationId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_WarehouseId",
                table: "purchase_orders",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_request_for_quotation_lines_RequestForQuotationId_VariantId",
                table: "request_for_quotation_lines",
                columns: new[] { "RequestForQuotationId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_request_for_quotation_lines_VariantId",
                table: "request_for_quotation_lines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_request_for_quotation_suppliers_SupplierOrganizationId",
                table: "request_for_quotation_suppliers",
                column: "SupplierOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_requests_for_quotation_AwardedQuotationId_Id",
                table: "requests_for_quotation",
                columns: new[] { "AwardedQuotationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_requests_for_quotation_Number",
                table: "requests_for_quotation",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_requests_for_quotation_Status_ResponseDeadline",
                table: "requests_for_quotation",
                columns: new[] { "Status", "ResponseDeadline" });

            migrationBuilder.CreateIndex(
                name: "IX_requests_for_quotation_WarehouseId",
                table: "requests_for_quotation",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_lines_LotId",
                table: "stock_transfer_lines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_lines_StockTransferId_VariantId_LotId",
                table: "stock_transfer_lines",
                columns: new[] { "StockTransferId", "VariantId", "LotId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_lines_VariantId",
                table: "stock_transfer_lines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_CreateIdempotencyKey",
                table: "stock_transfers",
                column: "CreateIdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_DispatchIdempotencyKey",
                table: "stock_transfers",
                column: "DispatchIdempotencyKey",
                unique: true,
                filter: "\"DispatchIdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_FromWarehouseId",
                table: "stock_transfers",
                column: "FromWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_Number",
                table: "stock_transfers",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_ReceiveIdempotencyKey",
                table: "stock_transfers",
                column: "ReceiveIdempotencyKey",
                unique: true,
                filter: "\"ReceiveIdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_Status_CreatedAt",
                table: "stock_transfers",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_ToWarehouseId",
                table: "stock_transfers",
                column: "ToWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_organizations_Code",
                table: "supplier_organizations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_product_sources_SupplierOrganizationId_VariantId_S~",
                table: "supplier_product_sources",
                columns: new[] { "SupplierOrganizationId", "VariantId", "SupplierSku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_product_sources_VariantId",
                table: "supplier_product_sources",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_quotation_lines_RequestForQuotationLineId_RequestF~",
                table: "supplier_quotation_lines",
                columns: new[] { "RequestForQuotationLineId", "RequestForQuotationId" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_quotation_lines_SupplierQuotationId_RequestForQuo~1",
                table: "supplier_quotation_lines",
                columns: new[] { "SupplierQuotationId", "RequestForQuotationLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_quotation_lines_SupplierQuotationId_RequestForQuot~",
                table: "supplier_quotation_lines",
                columns: new[] { "SupplierQuotationId", "RequestForQuotationId" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_quotations_Number",
                table: "supplier_quotations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_quotations_RequestForQuotationId_SupplierOrganizat~",
                table: "supplier_quotations",
                columns: new[] { "RequestForQuotationId", "SupplierOrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_quotations_SupplierOrganizationId",
                table: "supplier_quotations",
                column: "SupplierOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_users_ApplicationUserId",
                table: "supplier_users",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_tasks_DestinationLocationId",
                table: "warehouse_tasks",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_tasks_LotId",
                table: "warehouse_tasks",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_tasks_Number",
                table: "warehouse_tasks",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_tasks_ReferenceType_ReferenceId",
                table: "warehouse_tasks",
                columns: new[] { "ReferenceType", "ReferenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_tasks_SourceLocationId",
                table: "warehouse_tasks",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_tasks_VariantId",
                table: "warehouse_tasks",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_tasks_WarehouseId_Status_Priority_CreatedAt",
                table: "warehouse_tasks",
                columns: new[] { "WarehouseId", "Status", "Priority", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_ledger_inventory_lots_LotId",
                table: "inventory_ledger",
                column: "LotId",
                principalTable: "inventory_lots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_fiscal_invoice_lines_fiscal_invoices_FiscalInvoiceId",
                table: "fiscal_invoice_lines",
                column: "FiscalInvoiceId",
                principalTable: "fiscal_invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_fiscal_invoice_lines_purchase_order_lines_PurchaseOrderLine~",
                table: "fiscal_invoice_lines",
                column: "PurchaseOrderLineId",
                principalTable: "purchase_order_lines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_fiscal_invoices_purchase_orders_PurchaseOrderId",
                table: "fiscal_invoices",
                column: "PurchaseOrderId",
                principalTable: "purchase_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_goods_receipt_lines_goods_receipts_GoodsReceiptId",
                table: "goods_receipt_lines",
                column: "GoodsReceiptId",
                principalTable: "goods_receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_goods_receipt_lines_purchase_order_lines_PurchaseOrderLineId",
                table: "goods_receipt_lines",
                column: "PurchaseOrderLineId",
                principalTable: "purchase_order_lines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_goods_receipts_inbound_shipments_InboundShipmentId",
                table: "goods_receipts",
                column: "InboundShipmentId",
                principalTable: "inbound_shipments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_goods_receipts_purchase_orders_PurchaseOrderId",
                table: "goods_receipts",
                column: "PurchaseOrderId",
                principalTable: "purchase_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inbound_shipment_lines_inbound_shipments_InboundShipmentId",
                table: "inbound_shipment_lines",
                column: "InboundShipmentId",
                principalTable: "inbound_shipments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_inbound_shipment_lines_purchase_order_lines_PurchaseOrderLi~",
                table: "inbound_shipment_lines",
                column: "PurchaseOrderLineId",
                principalTable: "purchase_order_lines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inbound_shipments_purchase_orders_PurchaseOrderId",
                table: "inbound_shipments",
                column: "PurchaseOrderId",
                principalTable: "purchase_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_invoice_matches_purchase_orders_PurchaseOrderId",
                table: "invoice_matches",
                column: "PurchaseOrderId",
                principalTable: "purchase_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_order_lines_purchase_orders_PurchaseOrderId",
                table: "purchase_order_lines",
                column: "PurchaseOrderId",
                principalTable: "purchase_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_order_lines_supplier_quotation_lines_SourceQuotati~",
                table: "purchase_order_lines",
                column: "SourceQuotationLineId",
                principalTable: "supplier_quotation_lines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_orders_supplier_quotations_SourceQuotationId_Suppl~",
                table: "purchase_orders",
                columns: new[] { "SourceQuotationId", "SupplierOrganizationId" },
                principalTable: "supplier_quotations",
                principalColumns: new[] { "Id", "SupplierOrganizationId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_request_for_quotation_lines_requests_for_quotation_RequestF~",
                table: "request_for_quotation_lines",
                column: "RequestForQuotationId",
                principalTable: "requests_for_quotation",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_request_for_quotation_suppliers_requests_for_quotation_Requ~",
                table: "request_for_quotation_suppliers",
                column: "RequestForQuotationId",
                principalTable: "requests_for_quotation",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_requests_for_quotation_supplier_quotations_AwardedQuotation~",
                table: "requests_for_quotation",
                columns: new[] { "AwardedQuotationId", "Id" },
                principalTable: "supplier_quotations",
                principalColumns: new[] { "Id", "RequestForQuotationId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_ledger_inventory_lots_LotId",
                table: "inventory_ledger");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_quotations_supplier_organizations_SupplierOrganiza~",
                table: "supplier_quotations");

            migrationBuilder.DropForeignKey(
                name: "FK_requests_for_quotation_supplier_quotations_AwardedQuotation~",
                table: "requests_for_quotation");

            migrationBuilder.DropTable(
                name: "cycle_count_lines");

            migrationBuilder.DropTable(
                name: "fiscal_invoice_lines");

            migrationBuilder.DropTable(
                name: "goods_receipt_lines");

            migrationBuilder.DropTable(
                name: "inbound_shipment_lines");

            migrationBuilder.DropTable(
                name: "inventory_balances");

            migrationBuilder.DropTable(
                name: "invoice_matches");

            migrationBuilder.DropTable(
                name: "request_for_quotation_suppliers");

            migrationBuilder.DropTable(
                name: "stock_transfer_lines");

            migrationBuilder.DropTable(
                name: "supplier_product_sources");

            migrationBuilder.DropTable(
                name: "supplier_users");

            migrationBuilder.DropTable(
                name: "warehouse_tasks");

            migrationBuilder.DropTable(
                name: "cycle_counts");

            migrationBuilder.DropTable(
                name: "goods_receipts");

            migrationBuilder.DropTable(
                name: "purchase_order_lines");

            migrationBuilder.DropTable(
                name: "fiscal_invoices");

            migrationBuilder.DropTable(
                name: "stock_transfers");

            migrationBuilder.DropTable(
                name: "inventory_lots");

            migrationBuilder.DropTable(
                name: "inbound_shipments");

            migrationBuilder.DropTable(
                name: "supplier_quotation_lines");

            migrationBuilder.DropTable(
                name: "purchase_orders");

            migrationBuilder.DropTable(
                name: "request_for_quotation_lines");

            migrationBuilder.DropTable(
                name: "supplier_organizations");

            migrationBuilder.DropTable(
                name: "supplier_quotations");

            migrationBuilder.DropTable(
                name: "requests_for_quotation");

            migrationBuilder.DropIndex(
                name: "IX_inventory_ledger_LotId",
                table: "inventory_ledger");

            migrationBuilder.DropColumn(
                name: "LotId",
                table: "inventory_ledger");

            migrationBuilder.DropColumn(
                name: "State",
                table: "inventory_ledger");
        }
    }
}
