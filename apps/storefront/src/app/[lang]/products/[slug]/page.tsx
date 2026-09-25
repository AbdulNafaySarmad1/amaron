import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { ProductDetailView } from "@/components/product/product-detail";
import { JsonLd } from "@/components/seo/json-ld";
import { ApiError, serverGet } from "@/lib/api";
import type { StorefrontProduct } from "@/lib/types";
import { withLocale } from "@/i18n/config";
import { currentDictionary, currentLocale } from "@/i18n/server";
import { alternates, breadcrumbJsonLd, productJsonLd, siteUrl } from "@/lib/seo";

export const dynamic = "force-dynamic";

async function loadProduct(slug: string) {
  try {
    return await serverGet<StorefrontProduct>(withLocale(`/api/storefront/products/${encodeURIComponent(slug)}`, await currentLocale()), 60);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    throw error;
  }
}

export async function generateMetadata({ params }: PageProps<"/[lang]/products/[slug]">): Promise<Metadata> {
  const [{ product }, locale] = await Promise.all([loadProduct((await params).slug), currentLocale()]);
  const title = product.seoTitle ?? product.title;
  const description = product.seoDescription ?? product.description;
  return { title, description, alternates: alternates(locale, `/products/${product.slug}`), openGraph: { title, description, locale } };
}

export default async function ProductPage({ params }: PageProps<"/[lang]/products/[slug]">) {
  const [data, locale, t] = await Promise.all([loadProduct((await params).slug), currentLocale(), currentDictionary()]);
  const { product } = data;
  const site = `${siteUrl()}/${locale}`;
  const url = `${site}/products/${product.slug}`;
  // Structured data comes from the same response the page renders, so it can never disagree with what shoppers see.
  return (
    <>
      <JsonLd data={productJsonLd(product, url)} />
      <JsonLd data={breadcrumbJsonLd([{ name: t.category.home, url: site }, { name: product.category, url: `${site}/c/${product.categorySlug}` }, { name: product.title, url }])} />
      <ProductDetailView data={data} />
    </>
  );
}
