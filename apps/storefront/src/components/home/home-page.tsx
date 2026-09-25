"use client";

import { Link } from "@/components/providers/locale-provider";
import { motion } from "motion/react";
import { ArrowIcon } from "@/components/icons";
import { HeroExperience } from "@/components/home/hero-experience";
import { ManifestoSequence } from "@/components/home/manifesto-sequence";
import { ProductGrid } from "@/components/product/product-grid";
import { motionTokens } from "@/lib/motion";
import type { HomeModel } from "@/lib/types";

export function HomePage({ home }: { home: HomeModel }) {
  const heroProduct = home.rails.flatMap((rail) => rail.products).find((product) => product.slug === home.hero?.productSlug) ?? home.rails[0]?.products[0];
  return (
    <main>
      <section className="hero">
        <motion.div className="hero__copy" initial={false} animate={{ opacity: 1, y: 0 }} transition={{ duration: motionTokens.duration.cinematic, ease: motionTokens.easing.enter }}>
          <p className="eyebrow">{home.hero?.eyebrow ?? "Useful, not ordinary"}</p>
          <h1>{home.hero?.title ?? "Better objects for everyday life"}</h1>
          <p>{home.hero?.subtitle ?? "Considered goods, fair prices, and delivery without the drama."}</p>
          <Link className="button button--primary button--large" href="/search">Shop the collection <ArrowIcon /></Link>
        </motion.div>
        <motion.div className="hero__stage" initial={false} animate={{ opacity: 1, x: 0 }} transition={{ duration: motionTokens.duration.cinematic, delay: 0.12, ease: motionTokens.easing.enter }}>
          {heroProduct ? <HeroExperience slug={heroProduct.slug} title={heroProduct.title} /> : null}
          <span className="hero__note">Selected with purpose<br />Built for repeat use</span>
        </motion.div>
      </section>

      <section className="category-strip" aria-label="Shop by category">
        {home.navigation.map((category, index) => <Link href={`/c/${category.slug}`} key={category.id}><span>{String(index + 1).padStart(2, "0")}</span>{category.name}<ArrowIcon /></Link>)}
      </section>

      {home.rails.map((rail) => (
        <section className="product-section" key={rail.id}>
          <header className="section-heading"><div><p className="eyebrow">The short list</p><h2>{rail.title}</h2></div><Link className="text-link" href="/search">See everything</Link></header>
          <ProductGrid products={rail.products} />
        </section>
      ))}

      <ManifestoSequence />
    </main>
  );
}
