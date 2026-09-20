import type { Metadata } from "next";
import { Fraunces, Manrope } from "next/font/google";
import { StorefrontProvider } from "@/components/providers/storefront-provider";
import { SiteHeader } from "@/components/shell/site-header";
import { serverGet } from "@/lib/api";
import type { Category } from "@/lib/types";
import "./globals.css";

const body = Manrope({ subsets: ["latin"], variable: "--font-body", display: "swap" });
const display = Fraunces({ subsets: ["latin"], variable: "--font-display", display: "swap" });

export const metadata: Metadata = {
  title: { default: "Amaron", template: "%s | Amaron" },
  description: "Considered goods for everyday rituals.",
};

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  let categories: Category[] = [];
  try { categories = await serverGet<Category[]>("/api/catalog/categories", 300); } catch { /* Navigation still works through All goods. */ }
  return <html lang="en" className={`${body.variable} ${display.variable}`}><body><StorefrontProvider><a className="skip-link" href="#main-content">Skip to main content</a><SiteHeader categories={categories} /><div id="main-content" tabIndex={-1}>{children}</div></StorefrontProvider></body></html>;
}
