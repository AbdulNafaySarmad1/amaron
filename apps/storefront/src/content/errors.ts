export const errorCopy = {
  product: { title: "We couldn't load this product.", body: "Try refreshing the page." },
  network: { title: "Looks like the connection dropped.", body: "Try again in a moment." },
  mutation: { title: "We couldn't complete that request.", body: "Nothing was charged or changed." },
  rateLimit: { title: "Too many attempts.", body: "Try again shortly." },
} as const;
