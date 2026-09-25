// API smoke test against a running development stack: node tests/e2e/api-smoke.mjs
// Walks catalog, cart, checkout and orders with the development identity (X-Customer-Id), and checks that the browser
// cannot set prices, that idempotency holds, that errors are RFC 7807, and that the BFF refuses anonymous and cross-site use.
const API = process.env.API_URL ?? "http://127.0.0.1:8080", WEB = process.env.APP_URL ?? "http://127.0.0.1:3000";
const customer = `smoke-${Date.now()}`;
let failures = 0;
const check = (name, ok, detail = "") => { console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? `  (${detail})` : ""}`); if (!ok) failures++; };
const api = async (path, init = {}) => {
  const response = await fetch(API + path, { ...init, headers: { "Content-Type": "application/json", "X-Customer-Id": customer, ...init.headers } });
  const text = await response.text();
  return { status: response.status, headers: response.headers, body: text ? JSON.parse(text) : null };
};

// Catalog
const categories = (await api("/api/catalog/categories?locale=ur")).body;
check("categories: 25 departments, translated", categories.filter((c) => !c.parentId).length === 25 && categories.find((c) => c.slug === "laptops").locale === "ur");
const byId = new Map(categories.map((c) => [c.id, c]));
const laptops = (await api("/api/catalog/products?category=laptops&pageSize=100")).body;
check("category isolation: laptops page holds only laptops", laptops.items.length > 0 && laptops.items.every((p) => p.kind === "laptop"), `${laptops.totalCount} products`);
const books = (await api("/api/catalog/products?category=books&q=laptop&pageSize=100")).body;
check("scoped search never leaves the category", books.items.every((p) => p.kind === "book"), `${books.totalCount} matches in books for "laptop"`);
const sum = async (parent) => { const kids = categories.filter((c) => c.parentId === parent.id); let n = 0; for (const k of kids) n += (await api(`/api/catalog/products?category=${k.slug}&pageSize=1`)).body.totalCount; return n; };
const computing = categories.find((c) => c.slug === "computing");
check("department = sum of its subcategories", (await api("/api/catalog/products?category=computing&pageSize=1")).body.totalCount === await sum(computing));
check("unknown category is empty, never 'everything'", (await api("/api/catalog/products?category=no-such-space")).body.totalCount === 0);
const search = (await api("/api/catalog/products?q=keybaord")).body;
check("typo search finds keyboards", search.items.some((p) => /keyboard/i.test(p.title)), `${search.totalCount} results`);
check("suggestions answer", (await api("/api/search/suggestions?q=refri")).body.length > 0);

// Product and structured data parity
const card = laptops.items.find((p) => p.availabilityHint !== "out_of_stock");
const detail = (await api(`/api/storefront/products/${card.slug}`)).body.product;
check("product detail matches its card", detail.slug === card.slug && detail.variants[0].price.amount === card.price.amount);
const html = await (await fetch(`${WEB}/en/products/${card.slug}`)).text();
const ld = [...html.matchAll(/<script type="application\/ld\+json"[^>]*>([\s\S]*?)<\/script>/g)].map((m) => JSON.parse(m[1])).find((d) => d["@type"] === "Product");
check("JSON-LD price and name come from the API", ld && Number(ld.offers.price) === detail.variants[0].price.amount && ld.name === detail.title && ld.sku === detail.variants[0].sku, ld ? `${ld.offers.price} ${ld.offers.priceCurrency}` : "no JSON-LD");
check("JSON-LD rating only with real reviews", detail.reviewCount > 0 ? Number(ld.aggregateRating.reviewCount) === detail.reviewCount : !ld.aggregateRating);

// Cart: the server prices everything
const variant = detail.variants[0];
const put = await api("/api/cart/items", { method: "PUT", body: JSON.stringify({ variantId: variant.id, quantity: 2, unitPrice: 0.01, price: 0.01 }) });
check("add to cart", put.status === 200);
const cart = (await api("/api/cart")).body;
const line = cart.items.find((i) => i.variantId === variant.id);
check("cart ignores prices sent by the browser", line && line.unitPrice.amount === variant.price.amount && cart.subtotal.amount === variant.price.amount * 2, `${line?.unitPrice.amount} x2 = ${cart.subtotal.amount}`);
const tooMany = await api("/api/cart/items", { method: "PUT", body: JSON.stringify({ variantId: variant.id, quantity: 100000 }) });
check("cart refuses more than can be sold, as RFC 7807", tooMany.status >= 400 && tooMany.status < 500 && tooMany.headers.get("content-type")?.includes("problem+json") && tooMany.body.code, `${tooMany.status} ${tooMany.body?.code}`);
const invalid = await api("/api/catalog/products?pageSize=1000");
check("invalid input is a 400 problem", invalid.status === 400 && invalid.headers.get("content-type")?.includes("problem+json"));

// Checkout and orders, idempotent
const address = { recipient: "Smoke Shopper", line1: "1 Market Street", city: "Karachi", region: "Sindh", postalCode: "74000", countryCode: "PK" };
const body = JSON.stringify({ shippingAddress: address, paymentMethod: { method: "Card", provider: "test" } });
const key = `smoke-${Date.now()}`;
const first = await api("/api/checkout/confirm", { method: "POST", headers: { "Idempotency-Key": key }, body });
const replay = await api("/api/checkout/confirm", { method: "POST", headers: { "Idempotency-Key": key }, body });
check("checkout places an order", first.status === 201, `${first.status} ${first.body?.order?.status ?? first.body?.code}`);
check("same idempotency key returns the same order", replay.body?.order?.id === first.body?.order?.id);
check("order subtotal is the server's", first.body?.order?.subtotal?.amount === variant.price.amount * 2, `${first.body?.order?.subtotal?.amount}`);
check("replay is flagged as a replay", replay.body?.idempotencyReplayed === true);
const orders = (await api("/api/orders")).body;
const orderList = orders.items ?? orders;
check("order appears in the customer's orders", orderList.some((o) => o.id === first.body.order.id));
const other = await fetch(`${API}/api/orders/${first.body.order.id}`, { headers: { "X-Customer-Id": "someone-else" } });
check("another customer cannot read the order", other.status === 404 || other.status === 403, `${other.status}`);
check("cart is empty after checkout", ((await api("/api/cart")).body.items ?? []).length === 0);

// BFF: authentication and CSRF
const bffAnon = await fetch(`${WEB}/api/bff/cart`);
check("BFF requires a session", bffAnon.status === 401);
const bffForged = await fetch(`${WEB}/api/bff/cart/items`, { method: "PUT", headers: { Origin: "https://evil.example", "Content-Type": "application/json" }, body: "{}" });
check("BFF refuses cross-site writes", bffForged.status === 401 || bffForged.status === 403, `${bffForged.status}`);
const notAllowed = await fetch(`${WEB}/api/bff/admin/operations/overview`);
check("BFF is not a general proxy", notAllowed.status === 404 || notAllowed.status === 401, `${notAllowed.status}`);

console.log(failures ? `\n${failures} FAILED` : "\nall passed");
process.exitCode = failures ? 1 : 0;
