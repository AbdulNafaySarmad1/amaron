using Commerce.Application;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Commerce.Infrastructure;

public sealed class CommerceDbContext(DbContextOptions<CommerceDbContext> options) : DbContext(options), ICommerceDbContext
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<InventoryItem> Inventory => Set<InventoryItem>();
    public DbSet<ProductAsset> ProductAssets => Set<ProductAsset>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasPostgresExtension("pg_trgm");
        model.Entity<Category>(e =>
        {
            e.ToTable("categories"); e.HasKey(x => x.Id); e.Property(x => x.Slug).HasMaxLength(160); e.Property(x => x.Name).HasMaxLength(160);
            e.HasIndex(x => x.Slug).IsUnique(); e.HasIndex(x => new { x.ParentId, x.SortOrder }); e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Product>(e =>
        {
            e.ToTable("products"); e.HasKey(x => x.Id); e.Property(x => x.Slug).HasMaxLength(160); e.Property(x => x.Title).HasMaxLength(240); e.Property(x => x.Brand).HasMaxLength(120); e.Property(x => x.Description).HasMaxLength(8000); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => x.Slug).IsUnique(); e.HasIndex(x => new { x.CategoryId, x.Status, x.Id }); e.HasIndex(x => x.Title).HasMethod("gin").HasOperators("gin_trgm_ops"); e.HasOne(x => x.Category).WithMany(x => x.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ProductVariant>(e =>
        {
            e.ToTable("product_variants"); e.HasKey(x => x.Id); e.Property(x => x.Sku).HasMaxLength(64); e.Property(x => x.Name).HasMaxLength(160); e.Property(x => x.Price).HasPrecision(19, 4); e.Property(x => x.ListPrice).HasPrecision(19, 4); e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasIndex(x => x.Sku).IsUnique(); e.HasIndex(x => new { x.ProductId, x.IsActive }); e.ToTable(t => t.HasCheckConstraint("ck_variants_price", "\"Price\" >= 0 AND (\"ListPrice\" IS NULL OR \"ListPrice\" >= \"Price\")")); e.HasOne(x => x.Product).WithMany(x => x.Variants).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<InventoryItem>(e =>
        {
            e.ToTable("inventory"); e.HasKey(x => x.VariantId); e.Property(x => x.Version).IsRowVersion(); e.ToTable(t => t.HasCheckConstraint("ck_inventory_nonnegative", "\"QuantityOnHand\" >= 0")); e.HasOne(x => x.Variant).WithOne(x => x.Inventory).HasForeignKey<InventoryItem>(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ProductAsset>(e =>
        {
            e.ToTable("product_assets"); e.HasKey(x => x.Id); e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30); e.Property(x => x.Url).HasMaxLength(1000); e.Property(x => x.MimeType).HasMaxLength(100); e.Property(x => x.Integrity).HasMaxLength(200); e.HasIndex(x => new { x.ProductId, x.SortOrder }); e.HasOne(x => x.Product).WithMany(x => x.Assets).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Review>(e =>
        {
            e.ToTable("reviews"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ProductId, x.IsApproved }); e.ToTable(t => t.HasCheckConstraint("ck_reviews_rating", "\"Rating\" BETWEEN 1 AND 5")); e.HasOne(x => x.Product).WithMany(x => x.Reviews).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Cart>(e =>
        {
            e.ToTable("carts"); e.HasKey(x => x.Id); e.Property(x => x.CustomerId).HasMaxLength(200); e.Property(x => x.Version).IsRowVersion(); e.HasIndex(x => x.CustomerId).IsUnique(); e.HasIndex(x => x.UpdatedAt);
        });
        model.Entity<CartItem>(e =>
        {
            e.ToTable("cart_items"); e.HasKey(x => new { x.CartId, x.VariantId }); e.ToTable(t => t.HasCheckConstraint("ck_cart_items_quantity", "\"Quantity\" BETWEEN 1 AND 99")); e.HasIndex(x => x.VariantId); e.HasOne(x => x.Cart).WithMany(x => x.Items).HasForeignKey(x => x.CartId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Order>(e =>
        {
            e.ToTable("orders"); e.HasKey(x => x.Id); e.Property(x => x.OrderNumber).HasMaxLength(32); e.Property(x => x.CustomerId).HasMaxLength(200); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength(); e.Property(x => x.Subtotal).HasPrecision(19, 4); e.Property(x => x.Recipient).HasMaxLength(120); e.Property(x => x.AddressLine1).HasMaxLength(200); e.Property(x => x.AddressLine2).HasMaxLength(200); e.Property(x => x.City).HasMaxLength(100); e.Property(x => x.Region).HasMaxLength(100); e.Property(x => x.PostalCode).HasMaxLength(24); e.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
            e.HasIndex(x => x.OrderNumber).IsUnique(); e.HasIndex(x => new { x.CustomerId, x.CreatedAt, x.Id });
        });
        model.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items"); e.HasKey(x => x.Id); e.Property(x => x.Sku).HasMaxLength(64); e.Property(x => x.ProductTitle).HasMaxLength(240); e.Property(x => x.VariantName).HasMaxLength(160); e.Property(x => x.UnitPrice).HasPrecision(19, 4); e.Property(x => x.LineTotal).HasPrecision(19, 4); e.ToTable(t => t.HasCheckConstraint("ck_order_items_quantity", "\"Quantity\" > 0")); e.HasIndex(x => x.OrderId); e.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_records"); e.HasKey(x => x.Id); e.Property(x => x.CustomerId).HasMaxLength(200); e.Property(x => x.Key).HasMaxLength(128); e.Property(x => x.RequestHash).HasMaxLength(64).IsFixedLength(); e.HasIndex(x => new { x.CustomerId, x.Key }).IsUnique(); e.HasIndex(x => x.CreatedAt);
        });
    }

    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken) => new ApplicationTransaction(await Database.BeginTransactionAsync(cancellationToken));
    public Task LockCartAsync(Guid cartId, CancellationToken cancellationToken) => Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM carts WHERE \"Id\" = {cartId} FOR UPDATE", cancellationToken);
    public Task LockInventoryAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken) => Database.ExecuteSqlRawAsync("SELECT 1 FROM inventory WHERE \"VariantId\" = ANY ({0}) ORDER BY \"VariantId\" FOR UPDATE", [variantIds.ToArray()], cancellationToken);

    private sealed class ApplicationTransaction(IDbContextTransaction transaction) : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
