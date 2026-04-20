<!--
Thanks for contributing to Zona-14. Before you submit, skim CONTRIBUTING.md
(especially §2 on _Zone14/ and §3 on // Zone14: markers). The checklist below
is what the CI validator and reviewers look for.

For general discussion, playtests, or uploading large media files, join our
Discord: https://discord.gg/CFVWFfVpJg
-->

## Summary

<!-- What does this PR change, and why? One or two paragraphs. -->

## Zona-14 convention checklist

- [ ] New files live under a `_Zone14/` folder (or this PR is a pure upstream port/merge — explain below).
- [ ] New C# namespaces match the `Content.<project>._Zone14.<Feature>.<Sub>` pattern.
- [ ] Edits to files **outside** `_Zone14/` carry `// Zone14:` (or `# Zone14:`) markers — see `CONTRIBUTING.md` §3.
- [ ] New or modified sprites / assets have a `meta.json` with populated `license` (SPDX) and `copyright` fields (see `CONTRIBUTING.md` §6); no existing `license` / `copyright` fields were removed.
- [ ] CI is green (`Zone14 convention check` workflow passes).

## Media

<!--
Attach before/after screenshots, GIFs, or short videos of the change in-game.
Small files (≤10 MB) can be dropped directly into this text area.

For larger videos, upload them to the Zona-14 Discord (https://discord.gg/CFVWFfVpJg)
and paste the message link here.

Tips:
- A side-by-side "before / after" GIF is worth a hundred words of testing notes.
- For UI changes, include both themes / zoom levels you tested.
- For audio changes, a short clip (or an uploaded .ogg link) is fine.
-->

- [ ] Screenshots / video attached — or mark **N/A** with one line explaining why (e.g., "server-only refactor, no visual change").

## Upstream source (if this is a port)

<!-- Link to the upstream PR / commit. Omit this section if this is greenfield Zona-14 work. -->

## Testing

<!-- How did you verify this works in-game? Servers joined, rounds played, edge cases exercised. -->
