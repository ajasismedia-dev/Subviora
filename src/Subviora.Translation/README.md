# Subviora.Translation

**Status: not implemented (AG-01 contains no translation code).**

Planned responsibility: implementations of `Subviora.Core.Translation.ITranslationProvider`.

- Offline-first: the default provider must run locally without network access.
- Optional online providers (opt-in, user-supplied credentials, never committed to the repository).
- Translation cache backed by SQLite (the cache store itself belongs in `Subviora.Infrastructure`).

No translation models, model weights or third-party API clients are part of the repository yet.

Allowed dependencies: `Subviora.Core` only (plus the selected inference runtime when implemented).
