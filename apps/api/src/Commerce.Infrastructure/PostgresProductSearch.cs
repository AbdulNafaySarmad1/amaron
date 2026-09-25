using Commerce.Application;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Infrastructure;

/// <summary>
/// A product matches exactly when all words appear anywhere (title, brand, description), when the title words start with
/// what was typed (autocomplete), when any word appears in the title or brand ("wireless noise cancelling headphones"
/// still finds the headphones), when the title contains the text, or when the shopper's translated title matches ("ہیڈفونز" finds the headphones on an Urdu page). Only when
/// nothing matches exactly does typo matching apply ("keybaord" finds the keyboard); applied always, it drags in noise
/// ("cable" would match "Portable Speaker" through "able"). Rank favours title hits and closeness.
/// Each way of matching is its own UNION branch so each can use its index (full-text GIN, trigram GIN); ranking runs only
/// on the candidates. Substring matching needs 3+ characters: trigram indexes cannot serve less, and shorter is mostly
/// noise that prefix matching covers better. Typo matching uses the trigram index through <%, whose cut-off is the
/// per-connection pg_trgm.word_similarity_threshold set from <see cref="TypoThreshold"/> (DependencyInjection).
/// </summary>
public sealed class PostgresProductSearch(CommerceDbContext db) : IProductSearch
{
    public const double TypoThreshold = 0.4;
    private const int TypoMinimumLength = 4;

    // {0} text, {1} LIKE pattern, {2} locale. English uses the 'english' configuration (stemming); translations use 'simple'.
    private const string AnyTerm = "nullif(replace(plainto_tsquery('english', {0})::text, '&', '|'), '')::tsquery";
    // Autocomplete: the last word as a prefix ("wireless he" -> wireless & he:*); NULL when the text has no words.
    private const string Prefix = "(nullif(plainto_tsquery('simple', {0})::text, '') || ':*')::tsquery";
    private const string SimpleAny = "nullif(replace(plainto_tsquery('simple', {0})::text, '&', '|'), '')::tsquery";
    private static readonly string Exact = $$"""
        SELECT p."Id" AS id FROM products p WHERE p.search_all @@ websearch_to_tsquery('english', {0})
        UNION SELECT p."Id" FROM products p WHERE p.search_title @@ {{AnyTerm}}
        UNION SELECT p."Id" FROM products p WHERE p.search_title @@ {{Prefix}}
        UNION SELECT p."Id" FROM products p WHERE length({0}) >= 3 AND lower(p."Title") LIKE {1} ESCAPE '\'
        UNION SELECT t."ProductId" FROM product_translations t WHERE t."Locale" = {2} AND t.search_title @@ {{SimpleAny}}
        UNION SELECT t."ProductId" FROM product_translations t WHERE t."Locale" = {2} AND t.search_title @@ {{Prefix}}
        UNION SELECT t."ProductId" FROM product_translations t WHERE t."Locale" = {2} AND length({0}) >= 3 AND lower(t."Title") LIKE {1} ESCAPE '\'
        """;
    private static readonly string Sql = $$"""
        SELECT p."Id" AS "ProductId",
               ts_rank(p.search_title, coalesce({{AnyTerm}}, ''::tsquery)) * 2
               + ts_rank(p.search_all, websearch_to_tsquery('english', {0}))
               + coalesce(ts_rank(t.search_title, coalesce({{SimpleAny}}, ''::tsquery)) * 2, 0)
               + greatest(word_similarity({0}, lower(p."Title")), coalesce(word_similarity({0}, lower(t."Title")), 0)) AS "Rank"
        FROM (
            {{Exact}}
            UNION SELECT p."Id" FROM products p WHERE length({0}) >= {{TypoMinimumLength}} AND {0} <% lower(p."Title") AND NOT EXISTS ({{Exact}})
            UNION SELECT t."ProductId" FROM product_translations t WHERE t."Locale" = {2} AND length({0}) >= {{TypoMinimumLength}} AND {0} <% lower(t."Title") AND NOT EXISTS ({{Exact}})
        ) c
        JOIN products p ON p."Id" = c.id
        LEFT JOIN product_translations t ON t."ProductId" = p."Id" AND t."Locale" = {2}
        """;

    public IQueryable<ProductMatch> Match(string query, string locale)
    {
        var text = query.Trim().ToLowerInvariant();
        var pattern = "%" + text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
        // A single composable SELECT (no CTE) so EF can join it into catalog queries; repeated placeholders share one parameter.
        return db.Database.SqlQuery<ProductMatch>(FormattableStringFactory.Create(Sql, text, pattern, locale));
    }
}
