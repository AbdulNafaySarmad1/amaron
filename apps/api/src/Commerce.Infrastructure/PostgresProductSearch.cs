using Commerce.Application;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Infrastructure;

/// <summary>
/// A product matches exactly when all words appear anywhere (title, brand, description), when any word appears in the
/// title or brand ("wireless noise cancelling headphones" still finds the headphones), when the title contains the
/// text, or when the shopper's translated title matches ("ہیڈفونز" finds the headphones on an Urdu page). Only when
/// nothing matches exactly does typo matching apply ("keybaord" finds the keyboard); applied always, it drags in noise
/// ("cable" would match "Portable Speaker" through "able"). Rank favours title hits and closeness.
/// Uses the generated search columns and GIN indexes from AddProductSearchVectors and AddCatalogTranslations.
/// </summary>
public sealed class PostgresProductSearch(CommerceDbContext db) : IProductSearch
{
    // ponytail: the typo branch calls word_similarity per row; move to the indexed <% operator if the catalog outgrows it.
    private const double TypoThreshold = 0.4;
    private const int TypoMinimumLength = 4;

    public IQueryable<ProductMatch> Match(string query, string locale)
    {
        var text = query.Trim().ToLowerInvariant();
        var pattern = "%" + text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
        // A single composable SELECT (no CTE) so EF can join it into catalog queries. English text uses the 'english'
        // configuration (stemming); translations use 'simple', which works for any language.
        return db.Database.SqlQuery<ProductMatch>($"""
            SELECT p."Id" AS "ProductId",
                   ts_rank(p.search_title, coalesce(q.any_term, ''::tsquery)) * 2
                   + ts_rank(p.search_all, q.all_terms)
                   + coalesce(ts_rank(t.search_title, coalesce(q.simple_any, ''::tsquery)) * 2, 0)
                   + greatest(word_similarity({text}, lower(p."Title")), coalesce(word_similarity({text}, lower(t."Title")), 0)) AS "Rank"
            FROM products p
            LEFT JOIN product_translations t ON t."ProductId" = p."Id" AND t."Locale" = {locale}
            CROSS JOIN LATERAL (
                SELECT websearch_to_tsquery('english', {text}) AS all_terms,
                       nullif(replace(plainto_tsquery('english', {text})::text, '&', '|'), '')::tsquery AS any_term,
                       nullif(replace(plainto_tsquery('simple', {text})::text, '&', '|'), '')::tsquery AS simple_any) q
            WHERE p.search_all @@ q.all_terms
               OR (q.any_term IS NOT NULL AND p.search_title @@ q.any_term)
               OR lower(p."Title") LIKE {pattern} ESCAPE '\'
               OR (q.simple_any IS NOT NULL AND t.search_title @@ q.simple_any)
               OR lower(t."Title") LIKE {pattern} ESCAPE '\'
               OR (length({text}) >= {TypoMinimumLength}
                   AND greatest(word_similarity({text}, lower(p."Title")), coalesce(word_similarity({text}, lower(t."Title")), 0)) >= {TypoThreshold}
                   AND NOT EXISTS (
                       SELECT 1 FROM products e
                       LEFT JOIN product_translations et ON et."ProductId" = e."Id" AND et."Locale" = {locale}
                       WHERE e.search_all @@ q.all_terms
                          OR (q.any_term IS NOT NULL AND e.search_title @@ q.any_term)
                          OR lower(e."Title") LIKE {pattern} ESCAPE '\'
                          OR (q.simple_any IS NOT NULL AND et.search_title @@ q.simple_any)
                          OR lower(et."Title") LIKE {pattern} ESCAPE '\'))
            """);
    }
}
