---
name: device-bridge-git-workflow
description: How to run git safely and efficiently against a repository reached through Cowork's device bridge (mcp__remote-devices__device_bash, working on a folder mounted from the user's own computer). Use this whenever you're about to run git commands (status, add, commit, push, worktree) on a mounted/bridged repository, not a repo living natively in your own sandbox. Covers the correct split of responsibility between you and the human for network operations, and — this is the part that will otherwise burn many turns — how to recognize and recover from the bridge's characteristic stale git lock files instead of misdiagnosing them as real conflicts or repeatedly retrying blind. Also covers why git worktrees created through the bridge can silently be unusable from the user's own shell.
---

# Device-bridge git workflow

The device bridge (`mcp__remote-devices__device_bash`) gives you a shell on the user's machine, but it reaches their files through a fuse-style mount rather than native filesystem access. Most of the time this is invisible. Git is where it isn't: the mount can create lock files as part of its normal, successful operations, but can't always delete them afterward — even when the git operation itself completed correctly. This produces alarming-looking errors that are usually not real problems, and the fix is almost always the same small ritual, not deep debugging.

## The split: what you do, what the human does

Local, filesystem-only git operations — `status`, `add`, `commit`, reading refs, `log`, local `worktree` management — work fine from the bridge and you should just do them.

Anything that talks to a remote (`push`, `fetch`, `pull`, `clone` over the network) is a different story: sandboxed environments commonly have network egress that reaches package registries but not arbitrary hosts like GitHub, and you'll typically see something like `403 from proxy after CONNECT` or a flat connection failure. Don't assume this is broken configuration to fix — attempt the operation once to confirm the current state, and if it fails on the network layer, hand the exact command to the user to run in their own terminal. This isn't a workaround, it's usually a real security boundary, and treating it as a bug to route around would be the wrong instinct.

After the user says they've pushed, don't just trust "done" — verify it objectively: `git rev-parse HEAD` locally against `git rev-parse origin/<branch>`, and report the actual hashes. This gives both of you a concrete, checkable confirmation instead of two people separately hoping the same thing happened.

## Recognizing the lock-file pattern

You'll see one or more of these, sometimes stacked across a few retries:

```
warning: unable to unlink '.../index.lock': Operation not permitted
warning: unable to unlink '.../objects/xx/tmp_obj_...': Operation not permitted
fatal: Unable to create '.../index.lock': File exists.
fatal: cannot lock ref 'HEAD': Unable to create '.../HEAD.lock': File exists.
```

The `warning:` lines alone are usually harmless — the operation completed (a commit went through, a stage succeeded) despite git failing to clean up its own temp/lock files afterward. The `fatal:` lines mean a *previous* operation's lock file is still sitting there blocking the *current* one from even starting. These are different severities; don't treat a bare warning as a reason to stop and ask for cleanup — check whether the operation actually succeeded first (e.g. `git log -1`, `git status`) before escalating.

## The recovery ritual

When you hit a `fatal:` lock error, don't loop retrying the same command — it will keep failing until the specific file is gone, and you cannot delete it yourself (the bridge blocks deletion from your side by design, the same way it blocks other destructive operations). Instead:

1. Read the exact path out of the error message — don't guess a generic `index.lock` at the repo root; it might be `HEAD.lock`, a ref-specific lock like `refs/heads/<branch>.lock`, or, if you're inside a worktree, a lock under `.git/worktrees/<name>/`.
2. Ask the user to remove that exact file from their own shell (PowerShell `Remove-Item <path> -Force -ErrorAction SilentlyContinue`, or the local shell equivalent), giving the literal command rather than a description.
3. Wait for their confirmation, then retry the *same* command you originally ran — don't skip straight past it to the next step, since the original operation may not have completed.
4. If it fails again with a *different* lock path, that's normal (git can lock several files across one logical operation) — repeat the ritual for the new path rather than treating it as the first fix having failed. If you want to save a round trip, `ls -la` the relevant `.git`/`.git/worktrees/<name>` directory yourself first and hand the user every `*.lock` file you can see in one message, rather than discovering them one at a time.

This can take several rounds on a single commit. That's a property of this bridge, not a sign that something is going wrong — say so if the user seems to be losing patience with the back-and-forth.

## Worktrees: a sharper version of the same trap

`git worktree add` writes absolute filesystem paths into its metadata (the worktree's own `.git` file, and the main repo's `.git/worktrees/<name>/` bookkeeping). If you create the worktree from the bridge, those paths are written from the bridge's own view of the filesystem — which is *not* the same as the path the user's OS sees for the same folder. The worktree will work fine when you operate on it through the bridge, and will fail with confusing errors (`fatal: not a git repository`, wrong-path errors) the moment the user tries to use it directly from their own shell.

Two implications worth knowing before you reach for a worktree at all:

- Only create a worktree at a path *inside* the bridged/mounted folder, never as a sibling directory — a sibling path typically isn't synced to the user's machine at all, so anything written there is invisible to them and disappears when the session ends.
- Even placed correctly, treat a bridge-created worktree as **your** tool, not something to hand the user for direct interaction. If they need to push a branch that only exists in that worktree, remember that `git push <remote> <branch>` operates on the ref, not the working directory — it can usually be run from whatever checkout the user normally uses, without ever entering the broken worktree. Once you're done with it, have the user delete both the worktree directory and its `.git/worktrees/<name>` metadata (again, deletion from their side, not yours) and run `git worktree prune`.
