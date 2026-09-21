using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installment_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProviderPlanId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    InstallmentCount = table.Column<int>(type: "integer", nullable: false),
                    Frequency = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CustomerCost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    MerchantCost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TermsReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installment_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "order_inventory_reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FulfilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_inventory_reservations", x => x.Id);
                    table.CheckConstraint("ck_order_inventory_reservation_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_order_inventory_reservations_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_inventory_reservations_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_inventory_reservations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_reconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProviderCapturedAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    ProviderRefundedAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvaluatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_reconciliations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessingStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_webhook_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    AmountAuthorized = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    AmountCaptured = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    AmountRefunded = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProviderPaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaymentMethodType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.CheckConstraint("ck_payment_amounts", "\"AmountAuthorized\" >= 0 AND \"AmountCaptured\" >= 0 AND \"AmountRefunded\" >= 0 AND \"AmountRefunded\" <= \"AmountCaptured\"");
                    table.ForeignKey(
                        name: "FK_payments_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "installment_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallmentPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProviderPaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installment_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_installment_schedules_installment_plans_InstallmentPlanId",
                        column: x => x.InstallmentPlanId,
                        principalTable: "installment_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureCategory = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuthenticationRequired = table.Column<bool>(type: "boolean", nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_attempts_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProviderRefundReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds", x => x.Id);
                    table.CheckConstraint("ck_refund_amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_refunds_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_installment_plans_PaymentId",
                table: "installment_plans",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_installment_schedules_InstallmentPlanId_Sequence",
                table: "installment_schedules",
                columns: new[] { "InstallmentPlanId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_inventory_reservations_ExpiresAt_ReleasedAt_Fulfilled~",
                table: "order_inventory_reservations",
                columns: new[] { "ExpiresAt", "ReleasedAt", "FulfilledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_order_inventory_reservations_OrderId_WarehouseId_VariantId",
                table: "order_inventory_reservations",
                columns: new[] { "OrderId", "WarehouseId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_inventory_reservations_VariantId",
                table: "order_inventory_reservations",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_order_inventory_reservations_WarehouseId",
                table: "order_inventory_reservations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_PaymentId_IdempotencyKey",
                table: "payment_attempts",
                columns: new[] { "PaymentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_reconciliations_PaymentId_EvaluatedAt",
                table: "payment_reconciliations",
                columns: new[] { "PaymentId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_Provider_ProviderEventId",
                table: "payment_webhook_events",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_OrderId",
                table: "payments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_Provider_ProviderPaymentReference",
                table: "payments",
                columns: new[] { "Provider", "ProviderPaymentReference" },
                unique: true,
                filter: "\"ProviderPaymentReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_PaymentId_IdempotencyKey",
                table: "refunds",
                columns: new[] { "PaymentId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installment_schedules");

            migrationBuilder.DropTable(
                name: "order_inventory_reservations");

            migrationBuilder.DropTable(
                name: "payment_attempts");

            migrationBuilder.DropTable(
                name: "payment_reconciliations");

            migrationBuilder.DropTable(
                name: "payment_webhook_events");

            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropTable(
                name: "installment_plans");

            migrationBuilder.DropTable(
                name: "payments");
        }
    }
}
