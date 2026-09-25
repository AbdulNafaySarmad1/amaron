import type { Metadata } from "next";
import { SavedList } from "@/components/saved/saved-list";
import { currentDictionary } from "@/i18n/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await currentDictionary()).saved.title, robots: { index: false } };
}

export default async function SavedPage() {
  const t = await currentDictionary();
  return (
    <main className="page">
      <header className="page__header">
        <h1 className="t-h1">{t.saved.title}</h1>
        <p className="t-body page__lede">{t.saved.lede}</p>
      </header>
      <SavedList />
    </main>
  );
}
