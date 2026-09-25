import { Link } from "@/components/providers/locale-provider";
import { currentDictionary } from "@/i18n/server";

export default async function NotFound() {
  const { notFound: copy } = await currentDictionary();
  return <main className="not-found"><span>404</span><h1>{copy.title}</h1><p>{copy.body}</p><Link className="button button--primary button--medium" href="/">{copy.home}</Link></main>;
}
