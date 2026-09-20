import http from "k6/http";
import { check } from "k6";

const baseUrl = __ENV.BASE_URL || "http://localhost:8080";
const variantId = "00000000-0000-0000-0000-000000001000";

export const options = {
  scenarios: {
    homepage: {
      executor: "constant-arrival-rate",
      exec: "homepage",
      rate: 1,
      timeUnit: "1s",
      duration: "20s",
      preAllocatedVUs: 2,
    },
    autocomplete: {
      executor: "constant-arrival-rate",
      exec: "autocomplete",
      rate: 2,
      timeUnit: "1s",
      duration: "20s",
      preAllocatedVUs: 3,
    },
    search: {
      executor: "constant-arrival-rate",
      exec: "search",
      rate: 1,
      timeUnit: "1s",
      duration: "20s",
      preAllocatedVUs: 2,
    },
    mixedCart: {
      executor: "constant-arrival-rate",
      exec: "mixedCart",
      rate: 1,
      timeUnit: "1s",
      duration: "20s",
      preAllocatedVUs: 3,
    },
  },
  thresholds: {
    checks: ["rate==1"],
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<1000", "p(99)<2000"],
  },
};

export function homepage() {
  const response = http.get(`${baseUrl}/api/storefront/home`);
  check(response, { "homepage returns 200": (result) => result.status === 200 });
}

export function autocomplete() {
  const terms = ["me", "po", "sm", "co"];
  const response = http.get(`${baseUrl}/api/search/suggestions?q=${terms[__ITER % terms.length]}`);
  check(response, { "autocomplete returns 200": (result) => result.status === 200 });
}

export function search() {
  const response = http.get(`${baseUrl}/api/catalog/products?q=desk&page=1&pageSize=12&sort=price-asc`);
  check(response, { "search returns 200": (result) => result.status === 200 });
}

export function mixedCart() {
  const headers = {
    "Content-Type": "application/json",
    "X-Customer-Id": `load-${__VU}`,
  };
  const browse = http.get(`${baseUrl}/api/catalog/products/noise-cancelling-headphones`);
  const cart = http.put(`${baseUrl}/api/cart/items`, JSON.stringify({ variantId, quantity: (__ITER % 3) + 1 }), { headers });
  check(browse, { "catalog read returns 200": (result) => result.status === 200 });
  check(cart, { "cart mutation returns 200": (result) => result.status === 200 });
}
