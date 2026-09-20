import Link from "next/link";

export default function NotFound() { return <main className="not-found"><span>404</span><p className="eyebrow">Wrong aisle</p><h1>That page isn&apos;t on the shelf.</h1><p>It may have moved, or it was never here to begin with.</p><Link className="button button--primary button--medium" href="/">Back to the storefront</Link></main>; }
