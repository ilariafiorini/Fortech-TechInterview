---
name: process-diary-from-transcript
description: How to turn a session's actual conversation history into a structured process document, work log, engineering diary, or "testimony" of how a task was done — for a portfolio, a handoff, a retrospective, or evidence of methodology. Use this whenever the user asks you to document, summarize, or write up "everything we did," "this whole conversation," "how this project came together," or similar — especially when they want it to show their own prompts/decisions and the reasoning behind what happened, not just a technical changelog. Also useful, on a smaller scale, any time you need to recall precisely what was discussed or decided earlier in a long session instead of relying on your own compressed memory of it.
---

# Process diary from transcript

The key move this skill makes: don't write this kind of document from memory alone. A long session compresses in your own context the same way it would in a human's — you'll remember the gist, get some wording wrong, and quietly conflate two different turns. When the user wants something they can call a *testimony*, accuracy about who said what, in what order, matters more than usual. Go get the real transcript.

## Locate the transcript

Session transcripts are typically stored as `.jsonl` files, one JSON object per line, under a per-project directory (for Claude Code / Cowork-style environments, something like `~/.claude/projects/<project-hash>/<session-id>.jsonl`). If you're not sure of the exact path, check whether the current session's context mentions one (a post-compaction summary often names the file it was compacted from) — that's usually your best lead. If several session files exist for the same project (multiple prior sessions), you may only have access to the most recent one; say so plainly rather than presenting a partial reconstruction as complete.

## Extract before you read

Don't `Read` the raw file directly — it's usually large, and most of it is tool_result payloads and system-reminder noise that would blow your context budget for no narrative value. Use the bundled `scripts/extract_transcript.py` instead:

```
python3 scripts/extract_transcript.py <path-to-transcript.jsonl> --out <workdir>
```

This produces three files worth knowing the difference between:

`user_prompts_full.txt` — every genuine human-typed prompt, verbatim, with timestamps. Use this when you need a prompt's exact original wording, typos and all — don't clean up or paraphrase quotes you're presenting as verbatim.

`digest.txt` — the same prompts, each followed by a compact list of what you (the assistant) actually did in response: tool calls with brief parameters, and any text you wrote. This is the file to read for the narrative — it's small enough to fit in context and rich enough to write an accurate account from.

`ask_user_question_log.txt` — every structured multiple-choice question you asked and the option(s) the user picked. These are real decisions even though the user didn't type free text for them, and are easy to miss if you only scan for typed prompts — a choice like "Logica basata su returnUrl (consigliato)" is exactly the kind of decision point a process document should surface.

The script filters out tool_result turns and the injected compact-summary entry that opens a post-compaction session, so what you get back is only things a human actually authored (typed text or a selected option) — read the script's docstring if you need to adapt the filtering for a differently-shaped transcript format.

## Be upfront about fidelity gaps

If the available transcript doesn't cover the whole period the user is asking about — a prior session got compacted into a prose summary and its raw transcript is gone, or only the last N turns are accessible — say so explicitly, before you start writing, not as a footnote at the end. Offer the honest options (reconstruct the missing part from whatever summary is available, clearly marked as reconstruction; or start the detailed record at whatever point verbatim data exists) and let the user choose. Presenting a reconstructed section with the same confidence as a verbatim one is the single easiest way to undermine a document whose whole purpose is to be trustworthy evidence.

## Ask before writing, not after

Format (a formal document vs. lightweight markdown) and how to handle any fidelity gap are real decisions that change the output materially — ask about them up front rather than guessing and redoing the work. Both are fast yes/no-ish questions, well suited to a single structured multiple-choice ask.

## Structure: chronological narrative, not a bare log dump

A raw list of "prompt → action" pairs is data, not a document. Organize it into phases that mirror how the work actually unfolded (a bug discovered → diagnosis discussed → solution implemented → verified → committed is a natural phase, not an arbitrary grouping), quote the user's own prompts rather than paraphrasing them where the request explicitly asks for "their prompts" to be tracked, and keep your own actions summarized at whatever altitude the user asked for ("in broad strokes" means two or three sentences per turn, not a blow-by-blow of every tool call).

## Close with synthesis, not just chronology

When the user's stated goal is to show "evolution" or "approach" — not just what happened but how the person worked — the most valuable part of the document is usually a closing section naming the patterns that repeat across many turns: where did they insist on discussion before code, where did they catch something you missed, where did they set an explicit boundary and hold to it later. This is the part that turns a transcript into an actual testimony of methodology, and it's exactly the part that requires you to have read the whole thing rather than skimmed it — don't skip it or leave it as a one-line closing sentence when the user has asked for it explicitly.
