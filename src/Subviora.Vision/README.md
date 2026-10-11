# Subviora.Vision

**Status: not implemented (AG-01 contains no OCR or classification code).**

Planned responsibility:

- Implementations of `Subviora.Core.Vision.IOcrProvider` (replaceable OCR engines).
- Implementations of `Subviora.Core.Vision.IUiClassificationProvider` — the hybrid Smart UI Detection
  system that combines profile regions, exclusion regions, layout heuristics and (optionally) a model
  to decide which recognised lines are real subtitles.

No OCR models or model weights are part of the repository. Model selection, licensing review and
distribution strategy are decided in later stages.

Allowed dependencies: `Subviora.Core` only (plus the selected OCR runtime when implemented).
