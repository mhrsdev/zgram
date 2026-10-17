# Security policy

Report vulnerabilities privately to the maintainers of this repository (use the repository's private security
advisory feature, or contact the operator of your deployment). Do not open a public issue for a security problem.

Scope notes:
- Security properties and their limits are documented in `docs/fa/security.md`. Zerogram does **not** provide
  end-to-end encryption; the server can read messages that are queued for delivery.
- Never include real secrets, TOTP keys, recovery codes or message content in reports.
- Issues in the unmodified upstream projects (Ferrite, Telegram for Android) should also be reported upstream.
