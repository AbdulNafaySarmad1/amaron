import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { TrackSearch } from "@/components/analytics/analytics";
import { FilterDisclosure } from "@/components/catalog/filter-disclosure";
import { JsonLd } from "@/components/seo/json-ld";
import { AdSlot } from "@/components/ads/ad-slot";
import { ProductGrid } from "@/components/product/product-grid";
import { Link } from "@/components/providers/locale-provider";
import { CATALOG_LANG, intlLocale, type Locale, localizePath, withLocale } from "@/i18n/config";
import { format, plural } from "@/i18n/dictionary";
import { currentDictionary, currentLocale } from "@/i18n/server";
import { serverGet } from "@/lib/api";
import { categoryPath, categoryTrail, childCategories } from "@/lib/categories";
import { alternates, breadcrumbJsonLd, siteUrl } from "@/lib/seo";
import type { Category, ProductPage } from "@/lib/types";

export const dynamic = "force-dynamic";

type Search = Record<string, string | string[] | undefined>;
const FILTER_KEYS = ["brand", "minPrice", "maxPrice", "minimumRating", "available"] as const;
const one = (search: Search, key: string) => { const value = search[key]; return (Array.isArray(value) ? value[0] : value)?.trim() || undefined; };
/** Detail filters ("RAM:16 GB") repeat: values of one label are alternatives, different labels all apply. */
const details = (search: Search) => [search.attr ?? []].flat().map((value) => value.trim()).filter((value) => value.includes(":")).slice(0, 12);

async function loadCategory(slug: string, locale: Locale) {
  const categories = await serverGet<Category[]>(withLocale("/api/catalog/categories", locale), 300);
  const trail = categoryTrail(categories, slug);
  const category = trail.at(-1);
  if (!category) notFound();
  return { categories, trail, category };
}

/** Canonical is the category (plus page); searched or filtered views are noindex so filter permutations never get indexed. */
export async function generateMetadata({ params, searchParams }: PageProps<"/[lang]/c/[slug]">): Promise<Metadata> {
  const [{ slug }, search, locale] = await Promise.all([params, searchParams as Promise<Search>, currentLocale()]);
  const { category } = await loadCategory(slug, locale);
  const page = Math.max(1, Number(one(search, "page")) || 1);
  const narrowed = Boolean(one(search, "q") || one(search, "sort") || FILTER_KEYS.some((key) => one(search, key)) || details(search).length);
  return {
    title: category.name,
    alternates: alternates(locale, `${categoryPath(category.slug)}${page > 1 ? `?page=${page}` : ""}`),
    robots: narrowed ? { index: false, follow: true } : undefined,
    openGraph: { title: category.name, locale },
  };
}

/** A category as its own shopping space: results never leave its subtree, and search stays inside it unless asked. */
export default async function CategoryPage({ params, searchParams }: PageProps<"/[lang]/c/[slug]">) {
  const [{ slug }, search, t, locale] = await Promise.all([params, searchParams as Promise<Search>, currentDictionary(), currentLocale()]);
  const { categories, trail, category } = await loadCategory(slug, locale);
  const c = t.category;
  const q = one(search, "q");
  const sort = one(search, "sort");
  const page = Math.max(1, Number(one(search, "page")) || 1);

  const query = new URLSearchParams({ category: category.slug, pageSize: "24", page: String(page) });
  if (q) query.set("q", q);
  if (sort) query.set("sort", sort);
  for (const key of FILTER_KEYS) { const value = one(search, key); if (value) query.set(key, value); }
  const chosen = details(search);
  for (const detail of chosen) query.append("attr", detail);
  const results = await serverGet<ProductPage>(withLocale(`/api/catalog/products?${query}`, locale), 20);

  const activeFilters = FILTER_KEYS.filter((key) => one(search, key)).length + chosen.length;
  const here = localizePath(locale, categoryPath(category.slug));
  const withParams = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams();
    for (const [key, value] of query) if (!["category", "pageSize"].includes(key)) next.append(key, value);
    for (const [key, value] of Object.entries(changes)) { if (value) next.set(key, value); else next.delete(key); }
    if (next.get("page") === "1") next.delete("page");
    const text = next.toString();
    return `${categoryPath(category.slug)}${text ? `?${text}` : ""}`;
  };
  const hidden = (keys: readonly string[]) => keys.map((key) => { const value = key === "q" ? q : key === "sort" ? sort : one(search, key); return value ? <input key={key} type="hidden" name={key} value={value} /> : null; });
  const hiddenDetails = chosen.map((detail) => <input key={detail} type="hidden" name="attr" value={detail} />);
  const children = childCategories(categories, category.id);
  const name = <span lang={category.locale} dir="auto">{category.name}</span>;

  return (
    <main className="page category-page">
      <JsonLd data={breadcrumbJsonLd([{ name: c.home, url: `${siteUrl()}/${locale}` }, ...trail.map((item) => ({ name: item.name, url: `${siteUrl()}/${locale}${categoryPath(item.slug)}` }))])} />
      <nav className="breadcrumbs" aria-label={c.breadcrumb}>
        <ol>
          <li><Link href="/">{c.home}</Link></li>
          {trail.map((item) => (
            <li key={item.id}>{item.id === category.id ? <span aria-current="page" lang={item.locale} dir="auto">{item.name}</span> : <Link href={categoryPath(item.slug)} lang={item.locale} dir="auto">{item.name}</Link>}</li>
          ))}
        </ol>
      </nav>

      <header className="category-page__header">
        <h1 className="t-h1">{q ? format(c.resultsFor, { query: q }) : name}</h1>
        <p className="t-meta">{q ? `${format(c.within, { category: category.name })} · ` : null}{plural(c.count, results.totalCount, intlLocale[locale])}</p>
        <form className="category-search" role="search" action={here}>
          <label className="sr-only" htmlFor="category-search">{format(c.searchIn, { category: category.name })}</label>
          <input id="category-search" name="q" type="search" defaultValue={q} placeholder={format(c.searchIn, { category: category.name })} autoComplete="off" enterKeyHint="search" />
          <button type="submit" className="button button--primary button--small">{c.search}</button>
        </form>
        {children.length ? (
          <nav className="chip-row" aria-label={format(c.subcategories, { category: category.name })}>
            {children.map((child) => <Link key={child.id} className="chip" href={categoryPath(child.slug)} lang={child.locale} dir="auto">{child.name}</Link>)}
          </nav>
        ) : null}
      </header>

      <div className="category-layout">
        <aside aria-label={c.filters}>
          <FilterDisclosure label={c.filters} activeCount={activeFilters}>
            <form className="filters" action={here}>
              {hidden(["q", "sort"])}
              {results.brands.length > 1 || one(search, "brand") ? (
                <fieldset>
                  <legend className="t-ui">{c.brand}</legend>
                  <label className="check-row"><input type="radio" name="brand" value="" defaultChecked={!one(search, "brand")} /> {c.anyBrand}</label>
                  {results.brands.map((brand) => (
                    <label className="check-row" key={brand.value}><input type="radio" name="brand" value={brand.value} defaultChecked={one(search, "brand") === brand.value} /> <span lang={CATALOG_LANG} dir="auto">{brand.value}</span> <small>{brand.count}</small></label>
                  ))}
                </fieldset>
              ) : null}
              {(results.attributes ?? []).map((facet) => (
                <fieldset key={facet.label}>
                  <legend className="t-ui" lang={CATALOG_LANG} dir="auto">{facet.label}</legend>
                  {facet.values.map((option) => {
                    const value = `${facet.label}:${option.value}`;
                    return <label className="check-row" key={value}><input type="checkbox" name="attr" value={value} defaultChecked={chosen.includes(value)} /> <span lang={CATALOG_LANG} dir="auto">{option.value}</span> <small>{option.count}</small></label>;
                  })}
                </fieldset>
              ))}
              <fieldset>
                <legend className="t-ui">{c.price}</legend>
                <div className="price-inputs">
                  <label><span>{c.min}</span><input type="number" name="minPrice" min="0" inputMode="decimal" defaultValue={one(search, "minPrice")} /></label>
                  <label><span>{c.max}</span><input type="number" name="maxPrice" min="0" inputMode="decimal" defaultValue={one(search, "maxPrice")} /></label>
                </div>
              </fieldset>
              <fieldset>
                <legend className="t-ui">{c.rating}</legend>
                <select name="minimumRating" defaultValue={one(search, "minimumRating") ?? ""} aria-label={c.rating}>
                  <option value="">{c.anyRating}</option>
                  <option value="4">{c.rating4}</option>
                  <option value="4.5">{c.rating45}</option>
                </select>
              </fieldset>
              <label className="check-row"><input type="checkbox" name="available" value="true" defaultChecked={one(search, "available") === "true"} /> {c.inStock}</label>
              <button className="button button--primary button--medium" type="submit">{c.apply}</button>
              {activeFilters ? <Link className="text-button" href={withParams(Object.fromEntries([...FILTER_KEYS, "attr"].map((key) => [key, undefined])))}>{c.clearFilters}</Link> : null}
            </form>
          </FilterDisclosure>
        </aside>

        <section className="results" aria-label={category.name}>
          <form className="sort-form" action={here}>
            {hidden(["q", ...FILTER_KEYS])}
            {hiddenDetails}
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
          {results.items.length ? <ProductGrid list={`category:${category.slug}`} products={results.items} /> : q && !activeFilters ? (
            // Never silently escape the category: say so, and offer the wider search explicitly.
            <div className="empty-state" role="status">
              <h2 className="t-h3">{format(c.noResultsIn, { category: category.name, query: q })}</h2>
              <p>{c.noResultsHint}</p>
              <Link className="button button--primary button--medium" href={`/search?q=${encodeURIComponent(q)}`}>{format(c.searchAllFor, { query: q })}</Link>
            </div>
          ) : (
            <div className="empty-state" role="status">
              <h2 className="t-h3">{c.noMatchTitle}</h2>
              <p>{c.noMatchBody}</p>
              <Link className="button button--secondary button--medium" href={withParams(Object.fromEntries([...FILTER_KEYS, "attr"].map((key) => [key, undefined])))}>{c.clearFilters}</Link>
            </div>
          )}

          {results.totalPages > 1 ? (
            <nav className="pagination" aria-label={format(c.pageOf, { page: results.page, total: results.totalPages })}>
              {results.page > 1 ? <Link href={withParams({ page: String(results.page - 1) })} rel="prev">{c.previous}</Link> : <span />}
              <span>{format(c.pageOf, { page: results.page, total: results.totalPages })}</span>
              {results.page < results.totalPages ? <Link href={withParams({ page: String(results.page + 1) })} rel="next">{c.next}</Link> : <span />}
            </nav>
          ) : null}
          <AdSlot placement="category-content" label={t.ads.label} />
        </section>
      </div>
    </main>
  );
}
