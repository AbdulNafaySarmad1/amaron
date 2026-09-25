import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { ProductDetailView } from "@/components/product/product-detail";
import { ApiError, serverGet } from "@/lib/api";
import type { StorefrontProduct } from "@/lib/types";

export const dynamic = "force-dynamic";

async function loadProduct(slug: string) {
  try {
    return await serverGet<StorefrontProduct>(`/api/storefront/products/${encodeURIComponent(slug)}`, 60);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    throw error;
  }
}

export async function generateMetadata({ params }: PageProps<"/[lang]/products/[slug]">): Promise<Metadata> {
  const { product } = await loadProduct((await params).slug);
  return { title: product.title, description: product.description };
}

export default async function ProductPage({ params }: PageProps<"/[lang]/products/[slug]">) {
  return <ProductDetailView data={await loadProduct((await params).slug)} />;
}
