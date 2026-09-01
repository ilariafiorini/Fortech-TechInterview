---
name: architecture-decision-log
description: How to maintain a living, rationale-rich decision log inside a project instead of letting design reasoning live only in chat history. Use this whenever a nontrivial technical or architectural decision gets made during a coding session — a new endpoint vs. a new service, a caching strategy, a data model change, a naming/UX decision with real alternatives, a deployment or configuration convention — especially in a project that already has (or should have) a docs/ folder. Write the entry in the same session the decision is made, not deferred to a later "write the docs" pass. Push toward using this any time you catch yourself explaining a design choice in prose to the user without also writing it down somewhere the next person (or the next session) can find it.
---

# Architecture decision log

The core idea: a codebase tells you *what* was built, but not *why*, and not what was considered and rejected. Six months later — or six turns later, in a long session — nobody can reconstruct the reasoning from the diff alone. This skill is about capturing that reasoning at the moment it's freshest, as a normal part of doing the work rather than as separate "documentation effort" tacked on afterward.

## Where it lives

If the project already has an architecture/decisions document, append to it — don't create a second, competing one. If it doesn't, propose creating one (e.g. `docs/architecture.md` or `docs/decisions.md`) rather than assuming the user wants it; some projects prefer per-decision files (classic ADR style, `docs/adr/0001-*.md`), others prefer one running document organized by topic or chronology. Either is fine — consistency with what the project already does matters more than which convention you pick.

## What makes an entry worth reading later

A good entry answers four questions, in this order, and doesn't skip the third even when it's tempting to:

**What problem forced this decision?** State the concrete trigger — a bug observed, a requirement, a constraint someone stated — not just "we decided to add caching." If a bug is involved, describe the actual observed symptom, not the internal mechanism you diagnosed (the mechanism belongs to the "what we chose" section, not the framing).

**What did you choose, and how does it actually work?** Concrete enough that someone unfamiliar with the last hour of conversation could locate the relevant code from the description — name the files, the functions, the endpoints.

**What did you *not* choose, and why not?** This is the section that gets skipped when people write documentation after the fact, because the rejected paths are already forgotten. It's also the most valuable section, because it's the one that prevents someone from re-proposing the same rejected idea in six months without knowing it was already tried and found wanting. Write it while the tradeoffs are still fresh — ideally in the same turn where the decision was made, not at the end of a long session when three other decisions have happened since.

**What does this decision cost or constrain going forward?** Even a good decision usually trades something away — a a slightly awkward duplication, a convention that only applies conditionally, a piece of technical debt accepted deliberately. Naming it explicitly (rather than letting it look like a costless win) is what makes the log trustworthy: readers learn to believe the "why" sections because the log doesn't pretend every choice was free.

## Style notes

Write it as prose with the reasoning embedded, not as a terse bullet list of facts — the reasoning *is* the point of the entry, and reasoning reads better as connected sentences than as fragments. Reference real file and symbol names so entries stay falsifiable against the actual code, not just plausible-sounding narrative.

If a later decision changes or reverses an earlier one, don't silently delete the old entry — either mark it superseded with a pointer to the new one, or fold the update into the existing entry with a note about what changed and when. A decision log that quietly contradicts itself is worse than no log at all, because it looks authoritative while being wrong.

## When *not* to log something

Not every code change is a decision. A straightforward bug fix with one obvious correct approach doesn't need an entry — the signal to log something is the presence of a real alternative that was seriously considered, or a piece of reasoning the user explicitly asked about or pushed back on. Logging everything indiscriminately buries the decisions that actually mattered.
