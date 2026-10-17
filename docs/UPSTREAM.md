# Upstream base / مبنای کد

| Component | Repository | Commit | License |
| --- | --- | --- | --- |
| Server (Ferrite) | https://github.com/aykutalparslan/Telegram-Server | `02b72d793ca10a2dab55e74d4bd938cdbd248b9f` (2026-09-24, "bug fix") | AGPL-3.0-or-later |
| Android client | https://github.com/DrKLO/Telegram | `62b56a07ca7e30e39f7fd00a6728d6bbd716ca1c` (v12.10.1, layer 229) | GPL-2.0-or-later (see upstream LICENSE) |

The first commit on this branch after the cleanup is a verbatim import
(`git archive`) of the server at the commit above, minus `docs/media` and the
iOS patch. Every later commit is a Zerogram change; `git diff <import-commit>..HEAD`
is the complete delta against upstream.
