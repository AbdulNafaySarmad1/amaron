import type { Metadata } from "next";
import { SavedList } from "@/components/saved/saved-list";

export const metadata: Metadata = { title: "Saved", robots: { index: false } };

export default function SavedPage() {
  return (
    <main className="page">
      <header className="page__header">
        <h1 className="t-h1">Saved</h1>
        <p className="t-body page__lede">Things worth a second look. Prices and availability are always current.</p>
      </header>
      <SavedList />
    </main>
  );
}
