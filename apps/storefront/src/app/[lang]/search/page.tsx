import { Link } from "@/components/providers/locale-provider";
import { TrackSearch } from "@/components/analytics/analytics";
import { FilterDisclosure } from "@/components/catalog/filter-disclosure";
import { ProductGrid } from "@/components/product/product-grid";
import { currentDictionary, currentLocale } from "@/i18n/server";
import { intlLocale, localizePath, withLocale } from "@/i18n/config";
import { format, plural } from "@/i18n/dictionary";
import { redirect } from "next/navigation";
import { serverGet } from "@/lib/api";
import { categoryPath } from "@/lib/categories";
import type { Category, ProductPage } from "@/lib/types";

export const dynamic = "force-dynamic";
// Result pages for arbitrary queries are thin and unbounded: keep them out of the index but let crawlers follow links.
export const metadata = { robots: { index: false, follow: true } };

type SearchParameters = Record<string, string | string[] | undefined>;
const FILTER_KEYS = ["minPrice", "maxPrice", "minimumRating", "available"] as const;

function value(parameters: SearchParameters, key: string) {
  const entry = parameters[key];
  return (Array.isArray(entry) ? entry[0] : entry)?.trim() || undefined;
}

/** Search across the whole store. Category pages own scoped browsing; this page is for queries and store-wide lists. */
export default async function SearchPage({ searchParams }: { searchParams: Promise<SearchParameters> }) {
  const [parameters, locale, t] = await Promise.all([searchParams, currentLocale(), currentDictionary()]);
  const c = t.category;
  // Categories have their own pages; keep old /search?category= links working by sending them there.
  const categoryParam = value(parameters, "category");
  if (categoryParam) {
    const rest = new URLSearchParams();
    for (const key of ["q", "brand", ...FILTER_KEYS, "sort", "page"]) { const entry = value(parameters, key); if (entry) rest.set(key, entry); }
    redirect(localizePath(locale, `${categoryPath(categoryParam)}${rest.size ? `?${rest}` : ""}`));
  }
  const q = value(parameters, "q");
  const sort = value(parameters, "sort");
  const query = new URLSearchParams({ pageSize: "24", page: String(Math.max(1, Number(value(parameters, "page")) || 1)) });
  if (q) query.set("q", q);
  if (sort) query.set("sort", sort);
  for (const key of FILTER_KEYS) { const entry = value(parameters, key); if (entry) query.set(key, entry); }
  const [results, categories] = await Promise.all([
    serverGet<ProductPage>(withLocale(`/api/catalog/products?${query}`, locale), 20),
    serverGet<Category[]>(withLocale("/api/catalog/categories", locale), 300),
  ]);

  const here = localizePath(locale, "/search");
  const activeFilters = FILTER_KEYS.filter((key) => value(parameters, key)).length;
  const withParams = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(query);
    next.delete("pageSize");
    for (const [key, entry] of Object.entries(changes)) { if (entry) next.set(key, entry); else next.delete(key); }
    if (next.get("page") === "1") next.delete("page");
    return `/search${next.size ? `?${next}` : ""}`;
  };
  const hidden = (keys: readonly string[]) => keys.map((key) => { const entry = key === "q" ? q : key === "sort" ? sort : value(parameters, key); return entry ? <input key={key} type="hidden" name={key} value={entry} /> : null; });
  const cleared = withParams(Object.fromEntries([...FILTER_KEYS, "page"].map((key) => [key, undefined])));

  return (
    <main className="page category-page">
      <header className="category-page__header">
        <h1 className="t-h1">{q ? format(c.resultsFor, { query: q }) : t.search.everything}</h1>
        <p className="t-meta">{plural(c.count, results.totalCount, intlLocale[locale])}</p>
        <nav className="chip-row" aria-label={t.nav.categories}>
          {categories.filter((item) => !item.parentId).map((item) => (
            <Link key={item.id} className="chip" href={`${categoryPath(item.slug)}${q ? `?q=${encodeURIComponent(q)}` : ""}`} lang={item.locale} dir="auto">{item.name}</Link>
          ))}
        </nav>
      </header>

      <div className="category-layout">
        <aside aria-label={c.filters}>
          <FilterDisclosure label={c.filters} activeCount={activeFilters}>
            <form className="filters" action={here}>
              {hidden(["q", "sort"])}
              <fieldset>
                <legend className="t-ui">{c.price}</legend>
                <div className="price-inputs">
                  <label><span>{c.min}</span><input type="number" name="minPrice" min="0" inputMode="decimal" defaultValue={value(parameters, "minPrice")} /></label>
                  <label><span>{c.max}</span><input type="number" name="maxPrice" min="0" inputMode="decimal" defaultValue={value(parameters, "maxPrice")} /></label>
                </div>
              </fieldset>
              <fieldset>
                <legend className="t-ui">{c.rating}</legend>
                <select name="minimumRating" defaultValue={value(parameters, "minimumRating") ?? ""} aria-label={c.rating}>
                  <option value="">{c.anyRating}</option>
                  <option value="4">{c.rating4}</option>
                  <option value="4.5">{c.rating45}</option>
                </select>
              </fieldset>
              <label className="check-row"><input type="checkbox" name="available" value="true" defaultChecked={value(parameters, "available") === "true"} /> {c.inStock}</label>
              <button className="button button--primary button--medium" type="submit">{c.apply}</button>
              {activeFilters ? <Link className="text-button" href={cleared}>{c.clearFilters}</Link> : null}
            </form>
          </FilterDisclosure>
        </aside>

        <section className="results" aria-label={t.search.allResults}>
          <form className="sort-form" action={here}>
            {hidden(["q", ...FILTER_KEYS])}
            <label htmlFor="sort">{c.sort}</label>
            <select id="sort" name="sort" defaultValue={sort ?? ""}>
              <option value="">{q ? t.search.relevance : c.sortName}</option>
              <option value="newest">{c.sortNewest}</option>
              <option value="rating">{c.sortRating}</option>
              <option value="price-asc">{c.sortPriceAsc}</option>
              <option value="price-desc">{c.sortPriceDesc}</option>
            </select>
            <button type="submit" className="text-button">{c.update}</button>
          </form>

          {q ? <TrackSearch term={q} /> : null}
          {results.items.length ? <ProductGrid list="search" products={results.items} /> : (
            <div className="empty-state" role="status">
              <h2 className="t-h3">{q && !activeFilters ? format(t.search.empty, { query: q }) : c.noMatchTitle}</h2>
              <p>{q && !activeFilters ? t.search.emptyHint : c.noMatchBody}</p>
              {activeFilters ? <Link className="button button--secondary button--medium" href={cleared}>{c.clearFilters}</Link> : null}
            </div>
          )}

          {results.totalPages > 1 ? (
            <nav className="pagination" aria-label={format(c.pageOf, { page: results.page, total: results.totalPages })}>
              {results.page > 1 ? <Link href={withParams({ page: String(results.page - 1) })} rel="prev">{c.previous}</Link> : <span />}
              <span>{format(c.pageOf, { page: results.page, total: results.totalPages })}</span>
              {results.page < results.totalPages ? <Link href={withParams({ page: String(results.page + 1) })} rel="next">{c.next}</Link> : <span />}
            </nav>
          ) : null}
        </section>
      </div>
    </main>
  );
}
