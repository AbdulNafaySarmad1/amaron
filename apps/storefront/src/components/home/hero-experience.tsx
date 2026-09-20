"use client";

import dynamic from "next/dynamic";
import { useReducedMotion } from "motion/react";
import { useEffect, useRef, useState } from "react";
import { ProductVisual } from "@/components/ui/product-visual";

const HeroScene = dynamic(() => import("@/components/home/hero-scene").then((module) => module.HeroScene).catch(() => () => null), { ssr: false });

function supportsWebGl() {
  try {
    const connection = (navigator as Navigator & { connection?: { saveData?: boolean } }).connection;
    if (connection?.saveData) return false;
    const canvas = document.createElement("canvas");
    return Boolean(canvas.getContext("webgl2") || canvas.getContext("webgl"));
  } catch { return false; }
}

export function HeroExperience({ slug, title }: { slug: string; title: string }) {
  const root = useRef<HTMLDivElement>(null);
  const reducedMotion = useReducedMotion();
  const [shouldLoad, setShouldLoad] = useState(false);
  const [active, setActive] = useState(false);

  useEffect(() => {
    if (reducedMotion || !supportsWebGl() || !root.current) return;
    const observer = new IntersectionObserver(([entry]) => {
      const visible = Boolean(entry?.isIntersecting);
      setActive(visible);
      if (visible) setShouldLoad(true);
    }, { rootMargin: "180px" });
    observer.observe(root.current);
    return () => observer.disconnect();
  }, [reducedMotion]);

  return <div ref={root} className="hero-experience"><ProductVisual slug={slug} title={title} />{shouldLoad ? <div className="hero-webgl" aria-hidden="true"><HeroScene active={active} /></div> : null}</div>;
}
