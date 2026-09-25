import { Fraunces, Manrope, Noto_Naskh_Arabic, Noto_Nastaliq_Urdu, Noto_Serif } from "next/font/google";

// Latin faces carry the brand. Script faces are not preloaded: their unicode-range means a browser
// downloads them only when Cyrillic, Arabic or Urdu text is actually on the page.
const body = Manrope({ subsets: ["latin", "cyrillic"], variable: "--font-body", display: "swap" });
const display = Fraunces({ subsets: ["latin"], variable: "--font-display", display: "swap" });
const serifCyrillic = Noto_Serif({ subsets: ["cyrillic"], variable: "--font-serif-cyrillic", display: "swap", preload: false });
const naskh = Noto_Naskh_Arabic({ subsets: ["arabic"], variable: "--font-naskh", display: "swap", preload: false });
const nastaliq = Noto_Nastaliq_Urdu({ subsets: ["arabic"], variable: "--font-nastaliq", display: "swap", preload: false });

export const fontVariables = [body, display, serifCyrillic, naskh, nastaliq].map((font) => font.variable).join(" ");
