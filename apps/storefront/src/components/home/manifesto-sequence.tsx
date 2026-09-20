"use client";

import { useReducedMotion } from "motion/react";
import { useEffect, useRef } from "react";

const principles = [
  ["01", "Useful by design", "Nothing ornamental unless it earns the space."],
  ["02", "Clear, fair pricing", "No countdown tricks. No mystery at checkout."],
  ["03", "Human support", "Plain answers from people who can actually help."],
] as const;

export function ManifestoSequence() {
  const root = useRef<HTMLElement>(null);
  const reducedMotion = useReducedMotion();
  useEffect(() => {
    if (reducedMotion || !root.current) return;
    let cleanup: (() => void) | undefined;
    let cancelled = false;
    void Promise.all([import("gsap"), import("gsap/ScrollTrigger")]).then(([gsapModule, scrollModule]) => {
      if (cancelled || !root.current) return;
      const gsap = gsapModule.default;
      gsap.registerPlugin(scrollModule.ScrollTrigger);
      const context = gsap.context(() => {
        const cards = gsap.utils.toArray<HTMLElement>(".manifesto-card");
        gsap.set(cards, { position: "absolute", inset: 0 });
        gsap.set(cards.slice(1), { yPercent: 105 });
        const timeline = gsap.timeline({ scrollTrigger: { trigger: root.current, start: "top top", end: `+=${cards.length * 70}%`, scrub: 0.7, pin: true, anticipatePin: 1 } });
        cards.slice(1).forEach((card, index) => timeline.to(card, { yPercent: 0, ease: "power2.inOut" }, index).to(cards[index]!, { scale: 0.94, opacity: 0.34, ease: "power2.inOut" }, index));
      }, root);
      cleanup = () => context.revert();
    }).catch(() => { /* The static sequence remains fully usable without GSAP. */ });
    return () => { cancelled = true; cleanup?.(); };
  }, [reducedMotion]);

  return <section ref={root} className="manifesto-sequence">
    <div className="manifesto-sequence__intro"><p className="eyebrow">Our standard</p><h2>Fewer things.<br /><em>Better chosen.</em></h2></div>
    <div className="manifesto-stack">{principles.map(([number, title, body]) => <article className="manifesto-card" key={number}><span>{number}</span><div><h3>{title}</h3><p>{body}</p></div></article>)}</div>
  </section>;
}
