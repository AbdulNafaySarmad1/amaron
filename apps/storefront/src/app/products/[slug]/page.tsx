import { notFound } from "next/navigation";
import { ProductDetailView } from "@/components/product/product-detail";
import { ApiError, serverGet } from "@/lib/api";
import type { StorefrontProduct } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function ProductPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  let data: StorefrontProduct;
  try {
    data = await serverGet<StorefrontProduct>(`/api/storefront/products/${encodeURIComponent(slug)}`, 60);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    throw error;
  }
  return <ProductDetailView data={data} />;
}
