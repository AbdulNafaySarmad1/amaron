import type { CSSProperties } from "react";
import { CATALOG_LANG } from "@/i18n/config";

function hueFor(value: string) {
  let hash = 0;
  for (const character of value) hash = (hash * 31 + character.charCodeAt(0)) | 0;
  return Math.abs(hash) % 360;
}

/** Placeholder art until real product photography exists: an object with a monogram, or a typeset book cover. */
export function ProductVisual({ slug, title, compact = false, variant = "object", byline }: { slug: string; title: string; compact?: boolean; variant?: "object" | "cover"; byline?: string }) {
  const style = { "--product-hue": hueFor(slug) } as CSSProperties;
  if (variant === "cover") {
    return (
      <div className="product-visual product-visual--cover" style={style} aria-hidden="true" lang={CATALOG_LANG} dir="auto">
        <span className="product-visual__cover">
          <span className="product-visual__cover-title">{title}</span>
          {byline ? <span className="product-visual__cover-byline">{byline}</span> : null}
        </span>
      </div>
    );
  }
  const monogram = title.split(/\s+/).slice(0, 2).map((word) => word[0]).join("");
  return (
    <div className={`product-visual ${compact ? "product-visual--compact" : ""}`} style={style} aria-hidden="true">
      <span className="product-visual__orbit" />
      <span className="product-visual__object"><span>{monogram}</span></span>
      <span className="product-visual__shadow" />
    </div>
  );
}
