import { appendFile, mkdir, readFile, writeFile } from "node:fs/promises";
import { basename, join } from "node:path";

const author = "AbdulNafaySarmad1";
const pendingPrompts = new Map();
const assistantMessages = new Map();
const assistantText = new Map();
let writeQueue = Promise.resolve();

function utc(timestamp = Date.now()) {
  return new Date(timestamp).toISOString();
}

function logPath(worktree, sessionID, timestamp) {
  const date = new Date(timestamp).toISOString().replace("T", "_").replace(/:/g, "-").slice(0, 19);
  return join(worktree, ".agent-logs", `${date}_${sessionID}.md`);
}

function entry(type, number, sessionID, timestamp, model, text) {
  return `[LOG_ENTRY type=${type} num=${number} session=${sessionID}]\ntimestamp: ${utc(timestamp)}\nmodel: ${model}\n\n${text}\n\n`;
}

async function appendTurn(worktree, turn) {
  const directory = join(worktree, ".agent-logs");
  await mkdir(directory, { recursive: true });
  const file = logPath(worktree, turn.sessionID, turn.promptTime);
  let content;

  try {
    content = await readFile(file, "utf8");
  } catch {
    const date = utc(turn.promptTime).slice(0, 10);
    content = `---\nsession_id: ${turn.sessionID}\ndate: ${date}\nauthor: ${author}\nmodel: ${turn.model}\ntool: opencode\nproject: ${basename(worktree)}\ntotal_exchanges: 0\nfirst_prompt_time: ${utc(turn.promptTime)}\nlast_prompt_time: ${utc(turn.promptTime)}\n---\n\n# Session Log - ${date}\n\nSession: \`${turn.sessionID}\` | Project: \`${basename(worktree)}\` | Author: \`${author}\`\n\n---\n\n`;
  }

  const exchanges = Number(content.match(/^total_exchanges: (\d+)$/m)?.[1] ?? 0) + 1;
  content = content
    .replace(/^total_exchanges: \d+$/m, `total_exchanges: ${exchanges}`)
    .replace(/^last_prompt_time: .*$/m, `last_prompt_time: ${utc(turn.responseTime)}`);
  content += entry("PROMPT", exchanges, turn.sessionID, turn.promptTime, turn.model, turn.prompt);
  content += entry("RESPONSE", exchanges, turn.sessionID, turn.responseTime, turn.model, turn.response);
  await writeFile(file, content, "utf8");
}

export const AgentCapturePlugin = async ({ worktree }) => ({
  "chat.message": async (input, output) => {
    const prompt = output.parts.filter((part) => part.type === "text").map((part) => part.text).join("");
    if (!prompt) return;
    pendingPrompts.set(input.sessionID, {
      prompt,
      promptTime: Date.now(),
      model: `${input.model?.providerID ?? "unknown"}/${input.model?.modelID ?? "unknown"}`,
    });
  },
  event: async ({ event }) => {
    if (event.type === "message.updated") {
      const message = event.properties.info;
      if (message.role === "assistant") assistantMessages.set(message.id, message);
      return;
    }

    if (event.type === "message.part.updated" && event.properties.part.type === "text") {
      const part = event.properties.part;
      if (!part.synthetic && !part.ignored) assistantText.set(part.messageID, part.text);
      return;
    }

    if (event.type !== "session.idle") return;
    const prompt = pendingPrompts.get(event.properties.sessionID);
    if (!prompt) return;

    const responseMessage = [...assistantMessages.values()]
      .filter((message) => message.sessionID === event.properties.sessionID && message.role === "assistant" && message.time.completed)
      .sort((left, right) => right.time.completed - left.time.completed)[0];
    const response = responseMessage ? assistantText.get(responseMessage.id) : undefined;
    if (!response) return;

    pendingPrompts.delete(event.properties.sessionID);
    writeQueue = writeQueue.then(() => appendTurn(worktree, {
      sessionID: event.properties.sessionID,
      prompt: prompt.prompt,
      promptTime: prompt.promptTime,
      model: responseMessage ? `${responseMessage.providerID}/${responseMessage.modelID}` : prompt.model,
      response,
      responseTime: responseMessage?.time.completed ?? Date.now(),
    }));
    await writeQueue;
  },
});
