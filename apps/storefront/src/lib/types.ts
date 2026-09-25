export type Money = { amount: number; currency: string };
export type ImageAsset = { url: string; mimeType: string; width: number | null; height: number | null };

export type Spec = { label: string; value: string };

export type ProductCardModel = {
  id: string;
  defaultVariantId: string;
  slug: string;
  title: string;
  brand: string;
  primaryImage: ImageAsset | null;
  price: Money;
  listPrice: Money | null;
  rating: number;
  reviewCount: number;
  availabilityHint: "in_stock" | "low_stock" | "out_of_stock";
  badges: string[];
  /** Presentation family, e.g. "book"; changes layout only. */
  kind: string;
  highlights: Spec[];
  /** Language the title is actually in (the requested one, or English as fallback). */
  locale: string;
};

export type Category = { id: string; slug: string; name: string; parentId: string | null; locale: string };
export type ProductRail = { id: string; title: string; products: ProductCardModel[] };
export type HomeModel = {
  navigation: Category[];
  hero: { eyebrow: string; title: string; subtitle: string; productSlug: string | null } | null;
  rails: ProductRail[];
  isDegraded: boolean;
};

export type Variant = { id: string; sku: string; name: string; price: Money; listPrice: Money | null; availabilityHint: string };
export type ProductAsset = { id: string; type: string; url: string; mimeType: string; width: number | null; height: number | null; sizeBytes: number | null; integrity: string | null; sortOrder: number };
export type ProductDetail = { id: string; slug: string; title: string; brand: string; description: string; category: string; categorySlug: string; categoryLocale: string; locale: string; seoTitle: string | null; seoDescription: string | null; kind: string; specifications: Spec[]; variants: Variant[]; assets: ProductAsset[]; rating: number; reviewCount: number };
export type RelatedProduct = { product: ProductCardModel; reason: string };
export type RelationshipGroup = { type: "accessory" | "compatible" | "complementary" | "alternative" | "upgrade" | "frequentlyBoughtWith"; items: RelatedProduct[] };
export type StorefrontProduct = { product: ProductDetail; recommendations: ProductCardModel[]; isDegraded: boolean; relationships: RelationshipGroup[] };
export type FacetValue = { value: string; count: number };
export type ProductPage = { items: ProductCardModel[]; page: number; pageSize: number; totalCount: number; totalPages: number; brands: FacetValue[] };
export type Suggestion = { type: "product" | "category"; value: string; slug: string | null; locale: string };

export type CartItem = { variantId: string; productId: string; slug: string; title: string; variant: string; image: ImageAsset | null; quantity: number; unitPrice: Money; lineTotal: Money; availabilityHint: string };
export type Cart = { cartId: string; totalQuantity: number; subtotal: Money; items: CartItem[]; version: string };
export type CartMutation = Omit<Cart, "items"> & { changedItem: CartItem | null };
export type Order = { id: string; orderNumber: string; status: string; subtotal: Money; createdAt: string; items: Array<{ variantId: string; sku: string; productTitle: string; variantName: string; quantity: number; unitPrice: Money; lineTotal: Money }> };
export type Payment = { id: string; orderId: string; customerId: string | null; authorized: Money; captured: Money; refunded: Money; status: string; provider: string; providerReference: string | null; method: string; createdAt: string; updatedAt: string; expiresAt: string; attempts: Array<{ id: string; provider: string; providerReference: string | null; amount: Money; method: string; status: string; failureCategory: string | null; authenticationRequired: boolean }> };
export type CheckoutResult = { order: Order; payment: Payment; idempotencyReplayed: boolean };
export type ShippingAddress = { recipient: string; line1: string; line2?: string; city: string; region: string; postalCode: string; countryCode: string };

export type AuthSession = { authenticated: false } | { authenticated: true; csrfToken: string; user: { name?: string } };

export type Problem = { title?: string; detail?: string; status?: number; code?: string; traceId?: string };
