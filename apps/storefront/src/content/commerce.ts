export const commerceCopy = {
  addToCart: { idle: "Add to cart", loading: "Adding…", success: "Added ✓", unavailable: "Currently unavailable" },
  cart: {
    title: "Your cart",
    emptyTitle: "Your cart is feeling a little empty",
    emptyBody: "Browse around and add something you like.",
    startShopping: "Start shopping",
    viewCart: "View cart",
    added: "Added to your cart",
    removed: "Removed from cart",
  },
  search: {
    placeholder: "Search products, brands and categories",
    empty: (query: string) => `We couldn't find anything for “${query}”.`,
    emptyHint: "Try checking the spelling or using fewer words.",
    loading: "Finding the best matches…",
  },
  availability: { in_stock: "In stock", low_stock: "Limited availability", out_of_stock: "Currently unavailable" },
} as const;
