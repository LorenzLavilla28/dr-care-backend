# API security decisions

The API is being designed against the OWASP API Security Top 10:

- BOLA: every lead lookup is scoped by organization; marketing agents are additionally scoped to assigned leads.
- Broken authentication: JWT issuer, audience, signature, and lifetime are validated; login is rate-limited.
- BOPLA: request DTOs are explicit allowlists. There is no generic patch endpoint for state, ownership, approvals, or prices.
- Unrestricted resource consumption: request bodies are capped and list pages are capped at 100 records.
- BFLA: application services enforce role and object access; controllers are not the only authorization boundary.
- Sensitive business flows: lead state changes are explicit commands and illegal state transitions return conflict responses.
- SSRF and unsafe file handling: document operations use private S3 presigned URLs (or signed local URLs only in Development) and ownership-bound object keys. Requests enforce allowlisted document types, extensions, content types, size limits, upload expiry, object existence, exact size, and server-side SHA-256 completion checks. Production must add magic-byte validation and quarantine/malware scanning before downstream use.
- Misconfiguration: production requires a non-empty 32-byte JWT signing key, uses safe problem responses, and does not seed users.
- Inventory: all current routes are under `/api/v1`; future modules must be versioned and documented before release.
- Unsafe API consumption: external email, payment, and AI adapters must validate responses, use timeouts, and avoid logging secrets or sensitive payloads.

Before production, add refresh-token rotation or organization SSO/MFA, a persistent outbox/idempotency store, audit retention policy, database migrations in the release pipeline, S3 malware scanning, centralized secrets, and automated API security tests.
