# Capture Test

Status: verified on 2026-09-21.

## Setup

- Tool: OpenCode 1.18.31 via API.
- Primary planning and execution model: `openai/gpt-5.6-terra`.
- Canary model: `opencode/mimo-v2.5-free`, used because the primary model was
  temporarily rate-limited.
- Mechanism: automatic project-level OpenCode lifecycle plugin. It records each
  `chat.message` prompt and writes the final assistant text when `session.idle`
  fires. It does not capture reasoning or tool calls.
- Config/plugin: `.opencode/plugins/agent-capture.js`; OpenCode automatically
  discovers project plugins in `.opencode/plugins/` at startup.
- Log path: `.agent-logs/`.

## Canary Log Files

- `.agent-logs/2026-09-21_16-50-14_ses_f3b1feb3cffe6G2ReT09rLT5Oo.md`
- `.agent-logs/2026-09-21_16-51-40_ses_f3b1e9ebcffe7AOMgqy36687tG.md`

## Canary Entries

[LOG_ENTRY type=PROMPT num=1 session=ses_f3b1feb3cffe6G2ReT09rLT5Oo]
timestamp: 2026-09-21T16:50:14.986Z
model: opencode/mimo-v2.5-free

"CAPTURE TEST — 8x assignment, AbdulNafaySarmad1"

[LOG_ENTRY type=RESPONSE num=1 session=ses_f3b1feb3cffe6G2ReT09rLT5Oo]
timestamp: 2026-09-21T16:51:24.415Z
model: opencode/mimo-v2.5-free

capture canary three

[LOG_ENTRY type=PROMPT num=1 session=ses_f3b1e9ebcffe7AOMgqy36687tG]
timestamp: 2026-09-21T16:51:40.120Z
model: opencode/mimo-v2.5-free

"CAPTURE TEST — 8x assignment, AbdulNafaySarmad1"

[LOG_ENTRY type=RESPONSE num=1 session=ses_f3b1e9ebcffe7AOMgqy36687tG]
timestamp: 2026-09-21T16:51:48.850Z
model: opencode/mimo-v2.5-free

Understood. Test received.

## Earlier Attempt

The earlier project setup left `.agent-logs/` ignored and produced 43
header-only log files without prompt or response entries. The required plugin
file was absent. The ignore rule was removed and the plugin was replaced with
the verified lifecycle implementation above.
