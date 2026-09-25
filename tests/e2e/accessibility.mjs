// Accessibility and layout on a running storefront: node tests/e2e/accessibility.mjs
// Drives headless Chromium over the DevTools protocol (no test framework): runs axe-core (WCAG 2.2 A/AA, including
// target size) and checks for horizontal overflow on phone and desktop widths in every language. Needs a Chromium or
// Chrome binary (CHROME, default: Playwright cache on Windows) and the storefront dependencies (for axe-core).
// SCREENSHOTS=<dir> saves one screenshot per page and viewport.
import { spawn } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { homedir, tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const WEB = process.env.APP_URL ?? "http://127.0.0.1:3000", API = process.env.API_URL ?? "http://127.0.0.1:8080";
const chrome = process.env.CHROME ?? join(homedir(), "AppData/Local/ms-playwright/chromium-1234/chrome-win64/chrome.exe");
const axe = readFileSync(join(dirname(fileURLToPath(import.meta.url)), "../../apps/storefront/node_modules/axe-core/axe.min.js"), "utf8");
const shots = process.env.SCREENSHOTS; if (shots) mkdirSync(shots, { recursive: true });
const profile = mkdtempSync(join(tmpdir(), "amaron-a11y-"));
const port = 9333;
const browser = spawn(chrome, ["--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, "--no-first-run", "--hide-scrollbars", "about:blank"], { stdio: "ignore" });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let target;
for (let i = 0; i < 50 && !target; i++) { try { target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find((t) => t.type === "page"); } catch { await sleep(200); } }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((r) => ws.addEventListener("open", r));
let id = 0; const pending = new Map(); const listeners = [];
ws.addEventListener("message", (e) => { const m = JSON.parse(e.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } else listeners.forEach((l) => l(m)); });
const send = (method, params = {}) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, (m) => (m.error ? reject(new Error(`${method}: ${m.error.message}`)) : resolve(m.result))); ws.send(JSON.stringify({ id: n, method, params })); });
const loaded = () => new Promise((r) => { const l = (m) => { if (m.method === "Page.loadEventFired") { listeners.splice(listeners.indexOf(l), 1); r(); } }; listeners.push(l); });
const evaluate = async (expression) => (await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true })).result.value;
await send("Page.enable"); await send("Runtime.enable"); await send("Page.setBypassCSP", { enabled: true });

const product = (await (await fetch(`${API}/api/catalog/products?category=headphones&pageSize=1`)).json()).items[0].slug;
const pages = ["/en", "/ur", "/ar", "/ur/c/laptops", "/ar/search?q=%D8%AB%D9%84%D8%A7%D8%AC%D8%A9", "/en/c/books?attr=Format%3AHardcover", "/en/c/laptops", "/ar/c/refrigerators?attr=Door%20configuration%3AFrench%20door", `/en/products/${product}`, `/ur/products/${product}`, "/en/search?q=lptop", "/ru/search", "/en/saved"];
const viewports = [{ name: "phone", width: 390, height: 844, mobile: true }, { name: "desktop", width: 1280, height: 800, mobile: false }];
let problems = 0;
for (const viewport of viewports) {
  await send("Emulation.setDeviceMetricsOverride", { width: viewport.width, height: viewport.height, deviceScaleFactor: 1, mobile: viewport.mobile });
  await send("Emulation.setTouchEmulationEnabled", { enabled: viewport.mobile });
  for (const path of pages) {
    const ready = loaded(); await send("Page.navigate", { url: WEB + path }); await ready; await sleep(700);
    const layout = await evaluate(`(() => {
      const width = document.documentElement.clientWidth;
      const wide = [...document.querySelectorAll("body *")].filter((el) => { const r = el.getBoundingClientRect(); return r.width > 0 && (r.right > width + 1 || r.left < -1) && getComputedStyle(el).position !== "fixed" && !el.closest("[popover], dialog:not([open]), .sr-only, [aria-hidden=true]") && !el.closest("[style*=overflow], .rail, .chip-row, .product-rail"); })
        .slice(0, 3).map((el) => el.tagName.toLowerCase() + (el.className ? "." + String(el.className).split(" ")[0] : "") + " right=" + Math.round(el.getBoundingClientRect().right));
      return { overflow: document.documentElement.scrollWidth > width + 1, scrollWidth: document.documentElement.scrollWidth, width, wide, lang: document.documentElement.lang, dir: document.documentElement.dir, h1: document.querySelectorAll("h1").length };
    })()`);
    await evaluate(axe);
    const result = await evaluate(`axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] }, resultTypes: ["violations"] }).then((r) => r.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length, target: v.nodes[0]?.target?.join(" "), summary: v.nodes[0]?.failureSummary?.split("\\n").slice(0, 2).join(" ") })))`);
    if (shots) writeFileSync(join(shots, `${viewport.name}${path.replace(/[^a-z0-9]+/gi, "_").slice(0, 60)}.png`), Buffer.from((await send("Page.captureScreenshot", { format: "png" })).data, "base64"));
    if (result.length || layout.overflow || layout.h1 !== 1) problems++;
    for (const v of result) console.log(`         ${v.id}: ${v.target} — ${v.summary}`);
    const flags = [layout.overflow ? `OVERFLOW ${layout.scrollWidth}>${layout.width} ${layout.wide.join(", ")}` : "", layout.h1 !== 1 ? `h1=${layout.h1}` : ""].filter(Boolean).join("; ");
    console.log(`${viewport.name.padEnd(8)} ${path.slice(0, 58).padEnd(58)} ${layout.lang}/${layout.dir}  axe:${result.length ? result.map((v) => `${v.id}(${v.impact},${v.nodes})`).join(" ") : "clean"}  ${flags}`);
  }
}
ws.close(); browser.kill(); await sleep(300); rmSync(profile, { recursive: true, force: true });
console.log(problems ? `${problems} page views with problems` : "all clean");
process.exitCode = problems ? 1 : 0;
