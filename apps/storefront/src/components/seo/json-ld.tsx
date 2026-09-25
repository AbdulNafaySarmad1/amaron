import { safeJsonLd } from "@/lib/seo";

/** Server-rendered structured data. Escaped so catalog text can never close the script element. */
export function JsonLd({ data }: { data: unknown }) {
  return <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: safeJsonLd(data) }} />;
}
