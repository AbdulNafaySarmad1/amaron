import type { SVGProps } from "react";

type IconProps = SVGProps<SVGSVGElement>;
const base = { width: 20, height: 20, viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", strokeWidth: 1.8, strokeLinecap: "round" as const, strokeLinejoin: "round" as const, "aria-hidden": true };

export function SearchIcon(props: IconProps) { return <svg {...base} {...props}><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></svg>; }
export function CartIcon(props: IconProps) { return <svg {...base} {...props}><path d="M3 4h2l2.2 10.2a2 2 0 0 0 2 1.6h7.9a2 2 0 0 0 2-1.6L20.5 8H6"/><circle cx="10" cy="20" r="1"/><circle cx="18" cy="20" r="1"/></svg>; }
export function HeartIcon(props: IconProps) { return <svg {...base} {...props}><path d="M20.8 4.6a5.4 5.4 0 0 0-7.6 0L12 5.8l-1.2-1.2a5.4 5.4 0 0 0-7.6 7.6l1.2 1.2L12 21l7.6-7.6 1.2-1.2a5.4 5.4 0 0 0 0-7.6Z"/></svg>; }
export function ArrowIcon(props: IconProps) { return <svg {...base} {...props}><path d="M5 12h14M14 7l5 5-5 5"/></svg>; }
export function StarIcon(props: IconProps) { return <svg {...base} {...props} fill="currentColor" strokeWidth="1"><path d="m12 2.8 2.7 5.5 6.1.9-4.4 4.3 1 6.1-5.4-2.9-5.4 2.9 1-6.1-4.4-4.3 6.1-.9L12 2.8Z"/></svg>; }
export function CloseIcon(props: IconProps) { return <svg {...base} {...props}><path d="m6 6 12 12M18 6 6 18"/></svg>; }
export function MinusIcon(props: IconProps) { return <svg {...base} {...props}><path d="M5 12h14"/></svg>; }
export function PlusIcon(props: IconProps) { return <svg {...base} {...props}><path d="M12 5v14M5 12h14"/></svg>; }
export function CheckIcon(props: IconProps) { return <svg {...base} {...props}><path d="m5 12 4 4L19 6"/></svg>; }
export function BagIcon(props: IconProps) { return <svg {...base} {...props}><path d="M5 8h14l-1 12H6L5 8Z"/><path d="M9 8V6.5a3 3 0 0 1 6 0V8"/></svg>; }
export function UserIcon(props: IconProps) { return <svg {...base} {...props}><circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/></svg>; }
export function HomeIcon(props: IconProps) { return <svg {...base} {...props}><path d="M4 11 12 4l8 7v9h-5v-6H9v6H4v-9Z"/></svg>; }
export function CompassIcon(props: IconProps) { return <svg {...base} {...props}><circle cx="12" cy="12" r="9"/><path d="m15.5 8.5-2 5-5 2 2-5 5-2Z"/></svg>; }
export function ChevronIcon(props: IconProps) { return <svg {...base} {...props}><path d="m6 9 6 6 6-6"/></svg>; }
export function ClockIcon(props: IconProps) { return <svg {...base} {...props}><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>; }
export function GlobeIcon(props: IconProps) { return <svg {...base} {...props}><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"/></svg>; }
