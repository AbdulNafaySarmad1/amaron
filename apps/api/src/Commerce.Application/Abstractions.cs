using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

public interface IApplicationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface ICommerceDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<InventoryItem> Inventory { get; }
    DbSet<ProductAsset> ProductAssets { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task LockCartAsync(Guid cartId, CancellationToken cancellationToken);
    Task LockInventoryAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken);
}

public interface IReadModelCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan expiration, IReadOnlyCollection<string> tags, CancellationToken cancellationToken);
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken);
}

public sealed class CommerceException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public static class CommerceErrors
{
    public static CommerceException NotFound(string resource) => new("resource_not_found", $"{resource} was not found.", 404);
    public static CommerceException Validation(string message) => new("validation_failed", message, 400);
    public static CommerceException Conflict(string code, string message) => new(code, message, 409);
}
