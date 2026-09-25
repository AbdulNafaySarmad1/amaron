import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { ProductDetailView } from "@/components/product/product-detail";
import { ApiError, serverGet } from "@/lib/api";
import type { StorefrontProduct } from "@/lib/types";
import { withLocale } from "@/i18n/config";
import { currentLocale } from "@/i18n/server";

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
  const { product } = await loadProduct((await params).slug);
  return { title: product.seoTitle ?? product.title, description: product.seoDescription ?? product.description };
}

export default async function ProductPage({ params }: PageProps<"/[lang]/products/[slug]">) {
  return <ProductDetailView data={await loadProduct((await params).slug)} />;
}
