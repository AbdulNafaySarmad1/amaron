import { Untranslated } from "@/components/i18n/untranslated";
import { Link } from "@/components/providers/locale-provider";
import { ProductGrid } from "@/components/product/product-grid";
import { currentLocale } from "@/i18n/server";
import { localizePath, withLocale } from "@/i18n/config";
import { redirect } from "next/navigation";
import { serverGet } from "@/lib/api";
import type { Category, ProductPage } from "@/lib/types";

export const dynamic = "force-dynamic";

type SearchParameters = Record<string, string | string[] | undefined>;

function value(parameters: SearchParameters, key: string) {
  const entry = parameters[key];
  return Array.isArray(entry) ? entry[0] : entry;
}

export default async function SearchPage({ searchParams }: { searchParams: Promise<SearchParameters> }) {
  const [parameters, locale] = await Promise.all([searchParams, currentLocale()]);
  const searchPath = localizePath(locale, "/search");
  // Categories have their own pages; keep old /search?category= links working by sending them there.
  const categoryParam = value(parameters, "category");
  if (categoryParam) {
    const rest = new URLSearchParams();
    for (const key of ["q", "brand", "minPrice", "maxPrice", "minimumRating", "available", "sort", "page"]) { const entry = value(parameters, key); if (entry) rest.set(key, entry); }
    redirect(localizePath(locale, `/c/${encodeURIComponent(categoryParam)}${rest.size ? `?${rest}` : ""}`));
  }
  const query = new URLSearchParams();
  for (const key of ["q", "category", "brand", "minPrice", "maxPrice", "minimumRating", "available", "sort", "page"]) {
    const entry = value(parameters, key);
    if (entry) query.set(key, entry);
  }
  query.set("pageSize", "24");
  const [results, categories] = await Promise.all([
    serverGet<ProductPage>(withLocale(`/api/catalog/products?${query.toString()}`, locale), 20),
    serverGet<Category[]>(withLocale("/api/catalog/categories", locale), 300),
  ]);
  const searchTerm = value(parameters, "q");
  const category = value(parameters, "category");
  const currentPage = results.page;

  function pageLink(page: number) {
    const next = new URLSearchParams(query);
    next.set("page", String(page));
    return `/search?${next.toString()}`;
  }

  return (
    <Untranslated>
    <main className="search-page">
      <header className="search-page__header">
        <p className="eyebrow">Browse the collection</p>
        <h1>{searchTerm ? <>Results for <em>“{searchTerm}”</em></> : category ? categories.find((item) => item.slug === category)?.name ?? "Products" : "All goods"}</h1>
        <p>{results.totalCount} {results.totalCount === 1 ? "good" : "goods"}, selected for daily use.</p>
      </header>
      <div className="search-layout">
        <aside className="filters">
          <form action={searchPath} method="get">
            {searchTerm ? <input type="hidden" name="q" value={searchTerm} /> : null}
            <fieldset><legend>Category</legend>{categories.filter((item) => !item.parentId).map((item) => <Link key={item.id} href={`/c/${item.slug}${searchTerm ? `?q=${encodeURIComponent(searchTerm)}` : ""}`}>{item.name}</Link>)}</fieldset>
            <fieldset><legend>Price</legend><div className="price-inputs"><label><span>Min</span><input type="number" name="minPrice" min="0" placeholder="$0" defaultValue={value(parameters, "minPrice")} /></label><label><span>Max</span><input type="number" name="maxPrice" min="0" placeholder="$500" defaultValue={value(parameters, "maxPrice")} /></label></div></fieldset>
            <fieldset><legend>Rating</legend><select name="minimumRating" defaultValue={value(parameters, "minimumRating") ?? ""}><option value="">Any rating</option><option value="4">4 stars and up</option><option value="4.5">4.5 stars and up</option></select></fieldset>
            <label className="check-row"><input type="checkbox" name="available" value="true" defaultChecked={value(parameters, "available") === "true"} /> In stock only</label>
            <button className="button button--primary button--medium" type="submit">Apply filters</button>
            <Link className="text-button" href={searchTerm ? `/search?q=${encodeURIComponent(searchTerm)}` : "/search"}>Clear filters</Link>
          </form>
        </aside>
        <section className="results" aria-label="Search results">
          <form className="sort-form" action={searchPath} method="get">
            {Array.from(query.entries()).filter(([key]) => !["sort", "page", "pageSize"].includes(key)).map(([key, entry]) => <input key={key} type="hidden" name={key} value={entry} />)}
            <label htmlFor="sort">Sort by</label><select id="sort" name="sort" defaultValue={value(parameters, "sort") ?? ""}><option value="">Name</option><option value="newest">Newest</option><option value="rating">Top rated</option><option value="price-asc">Price: low to high</option><option value="price-desc">Price: high to low</option></select><button type="submit">Update</button>
          </form>
          {results.items.length ? <ProductGrid products={results.items} /> : <div className="empty-results"><span>?</span><h2>Nothing matched that combination</h2><p>Try removing a filter or searching with fewer words.</p><Link className="button button--secondary button--medium" href="/search">Browse all goods</Link></div>}
          {results.totalPages > 1 ? <nav className="pagination" aria-label="Results pages"><Link aria-disabled={currentPage === 1} href={currentPage > 1 ? pageLink(currentPage - 1) : pageLink(1)}>Previous</Link><span>Page {currentPage} of {results.totalPages}</span><Link aria-disabled={currentPage === results.totalPages} href={currentPage < results.totalPages ? pageLink(currentPage + 1) : pageLink(results.totalPages)}>Next</Link></nav> : null}
        </section>
      </div>
    </main>
    </Untranslated>
  );
}
