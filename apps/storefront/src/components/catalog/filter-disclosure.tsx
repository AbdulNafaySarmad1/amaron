"use client";

import { useId, useState } from "react";

/** Filters sit beside results on wide screens. On phones they fold behind one button so results come first. */
export function FilterDisclosure({ label, activeCount, children }: { label: string; activeCount: number; children: React.ReactNode }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  return (
    <div className="filter-disclosure" data-open={open || undefined}>
      <button type="button" className="button button--secondary button--medium filter-disclosure__toggle" aria-expanded={open} aria-controls={id} onClick={() => setOpen((x) => !x)}>
        {label}{activeCount ? <b>{activeCount}</b> : null}
      </button>
      <div id={id} className="filter-disclosure__panel">{children}</div>
    </div>
  );
}
