import { ArrowIcon } from "@/components/icons";
import { HomeSearch } from "@/components/home/home-search";
import { RecentlyExplored } from "@/components/home/recently-explored";
import { ProductGrid } from "@/components/product/product-grid";
import { Link } from "@/components/providers/locale-provider";
import { withLocale } from "@/i18n/config";
import { format } from "@/i18n/dictionary";
import { currentDictionary, currentLocale } from "@/i18n/server";
import { serverGet } from "@/lib/api";
import { categoryPath, childCategories } from "@/lib/categories";
import type { HomeModel, ProductCardModel, ProductPage } from "@/lib/types";

export const dynamic = "force-dynamic";

const RAIL_SIZE = 4;

/** Optional rails fail quietly: a slow query must never take the homepage down with it. */
const optional = <T,>(promise: Promise<T>) => promise.catch(() => null);

/** Few products per section, no repeats across sections, and nothing without real data behind it. */
export default async function StorefrontPage() {
  const locale = await currentLocale();
  const [home, newest, rated, t] = await Promise.all([
    serverGet<HomeModel>(withLocale("/api/storefront/home", locale), 60),
    optional(serverGet<ProductPage>(withLocale(`/api/catalog/products?sort=newest&pageSize=${RAIL_SIZE * 3}`, locale), 60)),
    optional(serverGet<ProductPage>(withLocale(`/api/catalog/products?sort=rating&pageSize=${RAIL_SIZE * 3}`, locale), 60)),
    currentDictionary(),
  ]);

  const shown = new Set<string>();
  const take = (products: ProductCardModel[] | undefined) => {
    const picked = (products ?? []).filter((p) => !shown.has(p.id)).slice(0, RAIL_SIZE);
    picked.forEach((p) => shown.add(p.id));
    return picked;
  };
  const rails = [
    { id: "featured", title: t.home.featured, products: take(home.rails.find((r) => r.id === "featured")?.products), href: null },
    { id: "newest", title: t.home.newest, products: take(newest?.items), href: "/search?sort=newest" },
    { id: "rated", title: t.home.rated, products: take(rated?.items), href: "/search?sort=rating" },
  ].filter((rail) => rail.products.length);
  const spaces = home.navigation.filter((c) => !c.parentId);

  return (
    <main className="home">
      <section className="home-hero" aria-labelledby="home-title">
        <h1 id="home-title" className="t-display">{t.home.heroTitle}</h1>
        <p className="home-hero__lede">{t.home.heroLede}</p>
        <HomeSearch />
      </section>

      {spaces.length ? (
        <section className="home-spaces" aria-labelledby="home-spaces">
          <h2 id="home-spaces" className="t-h2">{t.home.spaces}</h2>
          <ul>
            {spaces.map((space) => (
              <li key={space.id}>
                <Link href={categoryPath(space.slug)} aria-label={format(t.home.explore, { category: space.name })}>
                  <span className="t-h3" lang={space.locale} dir="auto">{space.name}</span>
                  <span className="home-spaces__children t-meta" dir="auto">{childCategories(home.navigation, space.id).slice(0, 3).map((c) => c.name).join(" · ")}</span>
                  <ArrowIcon className="flip-rtl" />
                </Link>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {rails.map((rail) => (
        <section className="home-rail" key={rail.id} aria-labelledby={`home-${rail.id}`}>
          <header className="home-rail__head">
            <h2 id={`home-${rail.id}`} className="t-h2">{rail.title}</h2>
            {rail.href ? <Link className="text-link" href={rail.href}>{t.home.seeAll}</Link> : null}
          </header>
          <ProductGrid products={rail.products} />
        </section>
      ))}

      <RecentlyExplored />
    </main>
  );
}
