# ADR-0001: Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-10-21
- **Author:** Ethan

## Context

Rewind is meant to run for 20 years and be maintained by one developer, and maybe someday by a grandkid. The code shows *what* Rewind does, but not *why* it was built that way. Without a record, a future maintainer can't tell a deliberate choice from an accident, and may undo something that was there for a good reason.

## Decision

Every significant decision gets an Architecture Decision Record in `docs/adr`, numbered in order (`0001-…`, `0002-…`) and written in Markdown.

Each ADR has:

- a title, a status (Proposed, Accepted or Superseded), a date and an author;
- **Context:** the problem and the forces at play;
- **Decision:** what we chose;
- **Alternatives:** what else we considered, and why we didn't pick it;
- **Consequences:** what gets easier, what gets harder, and what we now have to do.

An accepted ADR is never rewritten. To change a decision, write a new ADR that supersedes the old one, and mark the old one's status as "Superseded by ADR-NNNN". Fixing a typo is fine.

A decision is significant when we chose between real alternatives, or when it shapes the architecture, the data or the tooling. The Project Overview's open decisions are the first candidates.

## Alternatives

- **No records, only the Overview and the Coding Standards.** Those describe the current design, but they get edited as it changes, so the reasoning behind old choices is lost.
- **Notes in commit messages or PR descriptions.** These are hard to find years later, and they're scattered across hundreds of commits.
- **A wiki outside the repo.** It can drift from the code, and it could disappear with the hosting service.

## Consequences

- The reasoning lives in the repo, is versioned with the code, and survives a move to another Git host.
- Writing an ADR takes a few minutes for each decision.
- Reviews check that a PR which changes the architecture also adds an ADR (DOC-1).
- This follows Michael Nygard's original ADR format, lightly extended with an Alternatives section.
