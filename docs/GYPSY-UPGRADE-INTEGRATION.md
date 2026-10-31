# Gypsy upgrade integration - 2026-10-30

UBlade3 adopted the new Gypsy Studio P1 surfaces released on 2026-10-30 rather than copying their behavior by hand.

## What Gypsy added and how UBlade3 uses it

| Gypsy capability | UBlade3 use | Verified surface |
| --- | --- | --- |
| Project bootstrap (`gypsy project init`) | Generated `project.gypsy.json` with engine unity 6000.6.7f1, target pc, hardware gate | `gypsy check --project` passes |
| Toolpacks/slices | `toolpacks/ublade3.unity.json` (checks, build, capture, exact-play) and `slices/ublade3.playable.json` | `toolpacks run` passed; `slice run` passed |
| Capability manifests | Four UBlade3 capabilities: build, checks, capture, exact runtime | `capabilities` validator: 4/4 valid |
| Action cache and bounded scheduler | Gypsy's runner owns the toolpack/slice state; the local state directory is under `workstream/state` and is gitignored | `workstream/state/toolpacks` and `slices` populated |
| Asset registry | `scripts/gypsy_u3.py assets` scans the Unity assets, docs, scripts and runtime | `UBLADE3_GYPSY_ASSETS_OK files=3123`, ~100 MB, provenance warning (no registry) |
| Metrics | `scripts/gypsy_u3.py metrics` aggregates the real toolpack/slice runs | `UBLADE3_GYPSY_METRICS_OK`, 8 runs |
| Local traces | `scripts/gypsy_u3.py traces` builds hierarchical state-derived traces | `UBLADE3_GYPSY_TRACES_OK traces=9` |
| Signed provenance + SBOM | `scripts/gypsy_u3.py provenance` HMAC-signs the built exe/artifacts and builds the manifest SBOM | `UBLADE3_GYPSY_PROVENANCE_OK artifacts=3 passed=True sbom_components=8`; `artifacts/ublade3-provenance.json`, `artifacts/ublade3-sbom.json` |
| Project check | `scripts/gypsy_u3.py check` runs Gypsy's own checker against UBlade3 | `checks: PASS | capabilities 50 | tools 16 | project UBlade3` |

## Commands

    python scripts/gypsy_u3.py check
    python scripts/gypsy_u3.py toolpacks list
    python scripts/gypsy_u3.py toolpacks run ublade3.unity
    python scripts/gypsy_u3.py slice run ublade3.playable
    python scripts/gypsy_u3.py assets
    python scripts/gypsy_u3.py metrics
    python scripts/gypsy_u3.py traces
    python scripts/gypsy_u3.py provenance

## Honest boundaries

Gypsy's newer features are local/read-only observability, signed with a local symmetric key and not PKI; the asset
registry is extension-based and the provenance warning for missing registry records is expected. The native exact
runtime is the playable path; Gypsy's UI quality gate is not applied to a screenshot because the game renders in a
separate native window. The UBlade3 C# port remains partial (see PORT-LEDGER). Nothing in GYPSY_HOME was modified.
