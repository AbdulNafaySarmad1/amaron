"use client";
export default function ErrorPage({ reset }: { reset: () => void }) { return <section className="page"><div className="panel state error-state"><strong>The workspace could not be opened</strong><p>Your session is unchanged. Retry the request or return to another workspace.</p><button className="primary" onClick={reset}>Try again</button></div></section>; }
