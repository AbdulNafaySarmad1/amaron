import { Untranslated } from "@/components/i18n/untranslated";
import { HomePage } from "@/components/home/home-page";
import { serverGet } from "@/lib/api";
import type { HomeModel } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function StorefrontPage() {
  const home = await serverGet<HomeModel>("/api/storefront/home", 60);
  return <Untranslated><HomePage home={home} /></Untranslated>;
}
