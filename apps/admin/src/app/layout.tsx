import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = { title: { default: "Amaron Operations", template: "%s | Amaron Operations" }, description: "Operational control plane" };
export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
