// Claude Code hook: appends each prompt and final response to .agent-logs/<first-prompt-time>_<session>.md
// Usage (from .claude/settings.json): node agent-capture.mjs session|prompt|response  < hook JSON on stdin
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, join } from "node:path";

const author = "AbdulNafaySarmad1";
const mode = process.argv[2];
const input = JSON.parse(readFileSync(0, "utf8"));
const sessionID = input.session_id;
const root = process.env.CLAUDE_PROJECT_DIR || input.cwd;
const dir = join(root, ".agent-logs");
const modelStash = join(tmpdir(), `claude-capture-model-${sessionID}`);
const now = new Date().toISOString();

function transcript() {
  try {
    return readFileSync(input.transcript_path, "utf8").split("\n").filter(Boolean).map((line) => JSON.parse(line));
  } catch {
    return [];
  }
}

function currentModel(lines) {
  const last = lines.findLast((e) => e.type === "assistant" && e.message?.model && e.message.model !== "<synthetic>");
  if (last) return last.message.model;
  try { return readFileSync(modelStash, "utf8"); } catch { return process.env.ANTHROPIC_MODEL || "unknown"; }
}

// Fallback when the Stop input lacks last_assistant_message: text of the last assistant message in the transcript.
function lastAssistantText(lines) {
  const texts = lines.filter((e) => e.type === "assistant" && Array.isArray(e.message?.content));
  const lastID = texts.findLast((e) => e.message.content.some((c) => c.type === "text"))?.message.id;
  return texts
    .filter((e) => e.message.id === lastID)
    .flatMap((e) => e.message.content.filter((c) => c.type === "text").map((c) => c.text))
    .join("\n");
}

function entry(type, num, model, text) {
  return `[LOG_ENTRY type=${type} num=${num} session=${sessionID}]\ntimestamp: ${now}\nmodel: ${model}\n\n${text}\n\n\n`;
}

if (mode === "session") {
  if (input.model) writeFileSync(modelStash, typeof input.model === "string" ? input.model : input.model.id ?? "unknown");
  process.exit(0);
}

mkdirSync(dir, { recursive: true });
const existing = readdirSync(dir).find((f) => f.endsWith(`_${sessionID}.md`));
const file = join(dir, existing ?? `${now.slice(0, 19).replace("T", "_").replace(/:/g, "-")}_${sessionID}.md`);
const lines = transcript();
const model = currentModel(lines);
const project = basename(root);
let content = existsSync(file)
  ? readFileSync(file, "utf8")
  : `---\nsession_id: ${sessionID}\ndate: ${now.slice(0, 10)}\nauthor: ${author}\nmodel: ${model}\ntool: claude-code\nproject: ${project}\ntotal_exchanges: 0\nfirst_prompt_time: ${now}\nlast_prompt_time: ${now}\n---\n\n# Session Log - ${now.slice(0, 10)}\n\nSession: \`${sessionID.slice(0, 8)}\` | Project: \`${project}\` | Author: \`${author}\`\n\n---\n\n`;
let num = Number(content.match(/^total_exchanges: (\d+)$/m)?.[1] ?? 0);

if (mode === "prompt") {
  num += 1;
  content = content
    .replace(/^total_exchanges: \d+$/m, `total_exchanges: ${num}`)
    .replace(/^last_prompt_time: .*$/m, `last_prompt_time: ${now}`);
  content += entry("PROMPT", num, model, input.prompt);
} else if (mode === "response") {
  const text = input.last_assistant_message || lastAssistantText(lines);
  if (!text) process.exit(0);
  content += entry("RESPONSE", num, model, text);
}
writeFileSync(file, content, "utf8");
