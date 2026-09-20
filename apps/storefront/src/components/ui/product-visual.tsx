import type { CSSProperties } from "react";

function hueFor(value: string) {
  let hash = 0;
  for (const character of value) hash = (hash * 31 + character.charCodeAt(0)) | 0;
  return Math.abs(hash) % 360;
}

export function ProductVisual({ slug, title, compact = false }: { slug: string; title: string; compact?: boolean }) {
  const style = { "--product-hue": hueFor(slug) } as CSSProperties;
  const monogram = title.split(/\s+/).slice(0, 2).map((word) => word[0]).join("");
  return (
    <div className={`product-visual ${compact ? "product-visual--compact" : ""}`} style={style} aria-hidden="true">
      <span className="product-visual__orbit" />
      <span className="product-visual__object"><span>{monogram}</span></span>
      <span className="product-visual__shadow" />
    </div>
  );
}
