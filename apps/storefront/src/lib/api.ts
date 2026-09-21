import type { Problem } from "@/lib/types";

const serverApiUrl = process.env.API_URL ?? "http://localhost:8080";
let csrfToken: string | null = null;

export function setCsrfToken(value: string | null) {
  csrfToken = value;
}

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly problem: Problem) {
    super(problem.detail ?? problem.title ?? "The request could not be completed.");
  }
}

export class RequestTimeoutError extends Error {
  constructor() { super("The request timed out. Try again."); }
}

function requestSignal(signal: AbortSignal | null | undefined, timeoutMs: number) {
  const timeout = AbortSignal.timeout(timeoutMs);
  return signal ? AbortSignal.any([signal, timeout]) : timeout;
}

export async function serverGet<T>(path: string, revalidate = 30, timeoutMs = 8_000): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${serverApiUrl}${path}`, { next: { revalidate }, headers: { Accept: "application/json" }, signal: requestSignal(undefined, timeoutMs) });
  } catch (error) {
    if (error instanceof DOMException && error.name === "TimeoutError") throw new RequestTimeoutError();
    throw error;
  }
  if (!response.ok) throw new ApiError(response.status, await safeProblem(response));
  return response.json() as Promise<T>;
}

export async function browserRequest<T>(path: string, init?: RequestInit, timeoutMs = 10_000): Promise<T> {
  const headers = new Headers(init?.headers);
  headers.delete("Authorization");
  headers.delete("X-Customer-Id");
  headers.delete("X-Admin");
  headers.set("Accept", "application/json");
  if (init?.body) headers.set("Content-Type", "application/json");
  const method = (init?.method ?? "GET").toUpperCase();
  if (!["GET", "HEAD", "OPTIONS"].includes(method)) {
    if (!csrfToken) throw new Error("Your session is not ready. Refresh the page and try again.");
    headers.set("X-CSRF-Token", csrfToken);
  }
  let response: Response;
  try {
    response = await fetch(path, { ...init, headers, credentials: "same-origin", signal: requestSignal(init?.signal, timeoutMs) });
  } catch (error) {
    if (error instanceof DOMException && error.name === "TimeoutError") throw new RequestTimeoutError();
    throw error;
  }
  if (!response.ok) throw new ApiError(response.status, await safeProblem(response));
  return response.status === 204 ? (undefined as T) : (response.json() as Promise<T>);
}

async function safeProblem(response: Response): Promise<Problem> {
  try { return await response.json() as Problem; }
  catch { return { status: response.status, title: response.statusText }; }
}

export function formatMoney(money: { amount: number; currency: string }, locale = "en-US") {
  return new Intl.NumberFormat(locale, { style: "currency", currency: money.currency }).format(money.amount);
}
