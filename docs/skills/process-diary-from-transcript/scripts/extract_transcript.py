#!/usr/bin/env python3
"""
Extract a condensed, chronological digest from a Claude session transcript (.jsonl).

Separates genuine human-typed prompts (and the answers they gave to structured
multiple-choice questions, e.g. AskUserQuestion) from tool_result noise, system
reminders, and the injected compact-summary entry that starts a post-compaction
session. Segments the transcript into turns, one per real user prompt, and for
each turn lists the assistant's tool calls (briefly) and text responses.

This exists because reading a raw transcript directly is usually too large to fit
in context, and eyeballing it risks missing the difference between something the
user actually typed and a tool result or auto-injected reminder that merely has
role="user" in the log. Read the produced digest instead of the raw file.

Usage:
    python3 extract_transcript.py <path-to-transcript.jsonl> [--out DIR]

Writes, into --out (default: ./transcript_digest/):
    user_prompts_full.txt   - every real user prompt, verbatim, with timestamps
    digest.txt              - the full per-turn digest (prompts + assistant actions)
    ask_user_question_log.txt - every AskUserQuestion call and the answer given

Read digest.txt first for the narrative. Fall back to user_prompts_full.txt if you
need a prompt's exact original wording without any assistant-turn material mixed in.
"""

import argparse
import json
import os
import sys


def load_entries(path):
    entries = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                entries.append(json.loads(line))
            except json.JSONDecodeError:
                continue
    return entries


def is_real_user_prompt(d):
    """True for an entry that represents something the human actually typed —
    excludes tool_result turns (isMeta) and the injected compact-summary entry
    that opens a post-compaction session (isCompactSummary)."""
    if d.get("type") != "user":
        return False
    if d.get("isCompactSummary") or d.get("isMeta"):
        return False
    msg = d.get("message", {})
    content = msg.get("content")
    if isinstance(content, str):
        return bool(content.strip())
    if isinstance(content, list):
        return any(
            isinstance(c, dict) and c.get("type") == "text" and c.get("text", "").strip()
            for c in content
        )
    return False


def get_text(d):
    msg = d.get("message", {})
    content = msg.get("content")
    if isinstance(content, str):
        return content
    texts = [c.get("text", "") for c in content if isinstance(c, dict) and c.get("type") == "text"]
    return "\n".join(texts)


def tool_brief(name, inp):
    """One-line summary of a tool call, tuned for the common Claude Code /
    Cowork tool names. Falls back to a generic truncated-JSON summary for
    anything unrecognized, so it degrades gracefully on unfamiliar tool sets."""
    if name in ("Read",):
        return f"Read({inp.get('file_path', '')})"
    if name in ("Edit",):
        return f"Edit({inp.get('file_path', '')})"
    if name in ("Write",):
        return f"Write({inp.get('file_path', '')})"
    if name == "Bash":
        return f"Bash: {inp.get('description', inp.get('command', ''))[:80]}"
    if name == "mcp__remote-devices__device_bash":
        return f"device_bash: {inp.get('command', '')[:100]}"
    if name == "Grep":
        return f"Grep({inp.get('pattern', '')!r} in {inp.get('path', '.')})"
    if name == "Glob":
        return f"Glob({inp.get('pattern', '')})"
    if name == "AskUserQuestion":
        qs = inp.get("questions", [])
        return "AskUserQuestion: " + " | ".join(q.get("question", "")[:80] for q in qs)
    if name == "Agent":
        return f"Agent[{inp.get('subagent_type', '?')}]: {inp.get('description', '')}"
    if name in ("WebSearch", "WebFetch"):
        return f"{name}: {inp.get('query', inp.get('url', ''))[:100]}"
    return f"{name}: {json.dumps(inp, ensure_ascii=False)[:100]}"


def extract_ask_user_questions(entries):
    """Pairs each AskUserQuestion tool_use with its answer, wherever the
    matching tool_result shows up (usually a few entries later)."""
    out = []
    for i, d in enumerate(entries):
        if d.get("type") != "assistant":
            continue
        content = d.get("message", {}).get("content")
        if not isinstance(content, list):
            continue
        for c in content:
            if not (isinstance(c, dict) and c.get("type") == "tool_use" and c.get("name") == "AskUserQuestion"):
                continue
            tool_id = c.get("id")
            answer_text = None
            for d2 in entries[i:i + 8]:
                if d2.get("type") != "user":
                    continue
                c2list = d2.get("message", {}).get("content")
                if not isinstance(c2list, list):
                    continue
                for item in c2list:
                    if isinstance(item, dict) and item.get("type") == "tool_result" and item.get("tool_use_id") == tool_id:
                        answer_text = item.get("content")
            out.append((d.get("timestamp"), c.get("input", {}), answer_text))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("transcript", help="Path to the session .jsonl transcript")
    ap.add_argument("--out", default="./transcript_digest", help="Output directory")
    args = ap.parse_args()

    entries = load_entries(args.transcript)
    os.makedirs(args.out, exist_ok=True)

    # 1. Verbatim user prompts
    prompts = []
    for d in entries:
        if is_real_user_prompt(d):
            prompts.append((d.get("timestamp"), d.get("uuid"), get_text(d)))
    with open(os.path.join(args.out, "user_prompts_full.txt"), "w", encoding="utf-8") as f:
        for i, (ts, uuid, t) in enumerate(prompts):
            f.write(f"=== [{i}] {ts} (uuid={uuid}) ===\n{t}\n\n")

    # 2. Per-turn digest
    boundaries = [i for i, d in enumerate(entries) if is_real_user_prompt(d)]
    boundaries.append(len(entries))
    lines = []
    for bi in range(len(boundaries) - 1):
        start, end = boundaries[bi], boundaries[bi + 1]
        d = entries[start]
        lines.append(f"\n\n########## TURN {bi} | {d.get('timestamp')} ##########")
        lines.append("USER PROMPT:")
        lines.append(get_text(d))
        lines.append("\nASSISTANT ACTIONS:")
        for j in range(start + 1, end):
            dd = entries[j]
            if dd.get("type") != "assistant":
                continue
            content = dd.get("message", {}).get("content")
            if not isinstance(content, list):
                continue
            for c in content:
                if not isinstance(c, dict):
                    continue
                if c.get("type") == "tool_use":
                    lines.append("  - TOOL: " + tool_brief(c.get("name"), c.get("input", {})))
                elif c.get("type") == "text":
                    t = c.get("text", "").strip()
                    if t:
                        lines.append("  - TEXT: " + t[:600].replace("\n", " "))
    with open(os.path.join(args.out, "digest.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines))

    # 3. AskUserQuestion decision log
    aqs = extract_ask_user_questions(entries)
    with open(os.path.join(args.out, "ask_user_question_log.txt"), "w", encoding="utf-8") as f:
        for ts, inp, answer in aqs:
            f.write(f"=== {ts} ===\n")
            for q in inp.get("questions", []):
                f.write(f"Q: {q.get('question')}\n")
                for opt in q.get("options", []):
                    f.write(f"   - {opt.get('label')}: {opt.get('description', '')}\n")
            f.write(f"ANSWER: {answer}\n\n")

    print(f"{len(prompts)} real user prompts, {len(boundaries) - 1} turns, {len(aqs)} AskUserQuestion calls.")
    print(f"Written to {args.out}/ (user_prompts_full.txt, digest.txt, ask_user_question_log.txt)")


if __name__ == "__main__":
    main()
