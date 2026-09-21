import Link from "next/link";
export default function NotFound() { return <main className="standalone-state"><p className="eyebrow">404 / NOT FOUND</p><h1>Workspace not found</h1><p>The requested administrative route does not exist.</p><Link className="primary" href="/admin">Return to overview</Link></main>; }
