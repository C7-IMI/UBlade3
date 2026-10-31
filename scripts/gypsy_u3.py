#!/usr/bin/env python3
"""Project-local Gypsy adapter for the UBlade3 Unity port.

Gypsy Studio's CLI resolves toolpacks, slices and capability manifests from its own checkout,
and its toolpack/slice runners hard-code that checkout as the project root. UBlade3 must
not modify GYPSY_HOME, so this adapter imports Gypsy's own modules read-only (no bytecode
writes) and runs THIS repository's manifests with project_root=<this repo> and state under
workstream/state. Validation, state schemas and the orchestrator are Gypsy's, not a copy.

Usage:
    python scripts/gypsy_unity.py check
    python scripts/gypsy_unity.py capabilities
    python scripts/gypsy_unity.py toolpacks list|check
    python scripts/gypsy_unity.py toolpacks run ublade3.unity [--action build|demo|capture|capture-check]
    python scripts/gypsy_unity.py slice list
    python scripts/gypsy_unity.py slice run ublade3.unity [--json]
"""

import argparse
import importlib
import json
import os
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DEFAULT_GYPSY_HOME = Path(r"C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\ProjectGypsy")


def resolve_gypsy_home(value):
    if value:
        return Path(value)
    if os.environ.get("GYPSY_HOME"):
        return Path(os.environ["GYPSY_HOME"])
    return DEFAULT_GYPSY_HOME


def gypsy_module(home, name="gypsy"):
    sys.dont_write_bytecode = True
    workstream = str(home / "workstream")
    if workstream not in sys.path:
        sys.path.insert(0, workstream)
    return importlib.import_module(name)


def state_dir():
    path = REPO / "workstream" / "state"
    path.mkdir(parents=True, exist_ok=True)
    return path


def command_check(home):
    import subprocess

    environment = dict(os.environ)
    environment["PYTHONDONTWRITEBYTECODE"] = "1"
    process = subprocess.run(
        [sys.executable, str(home / "workstream" / "gypsy.py"), "check", "--project", str(REPO)],
        cwd=str(REPO),
        capture_output=True,
        text=True,
        env=environment,
        timeout=600,
    )
    if process.stdout.strip():
        print(process.stdout.strip())
    if process.stderr.strip():
        print(process.stderr.strip(), file=sys.stderr)
    print("UBLADE3_GYPSY_CHECK exit={}".format(process.returncode))
    return process.returncode


def command_capabilities(home):
    gypsy = gypsy_module(home)
    capabilities = []
    for path in sorted((REPO / "capabilities").glob("*.json")):
        document = json.loads(path.read_text(encoding="utf-8-sig"))
        document["_file"] = path.name
        capabilities.append(document)
    errors = gypsy.validate_capabilities(capabilities)
    for error in errors:
        print("FAIL " + error)
    print("UBLADE3_CAPABILITIES count={} errors={}".format(len(capabilities), len(errors)))
    print("UBLADE3_CAPABILITIES_OK" if not errors else "UBLADE3_CAPABILITIES_FAIL")
    return 0 if not errors else 1


def load_toolpacks(home):
    toolpacks = gypsy_module(home, "toolpacks")
    manifests = toolpacks.load_all(REPO / "toolpacks")
    return toolpacks, manifests


def command_toolpacks(home, args):
    toolpacks, manifests = load_toolpacks(home)
    if args.toolpacks_command == "list":
        for toolpack_id in sorted(manifests):
            manifest = manifests[toolpack_id]
            print("{}: {} (actions: {})".format(
                toolpack_id,
                manifest.get("name"),
                ", ".join(action["id"] for action in manifest.get("actions", []))))
        return 0
    if args.toolpacks_command == "check":
        problems = {}
        for toolpack_id, manifest in manifests.items():
            errors = toolpacks.validate_manifest(manifest)
            if errors:
                problems[toolpack_id] = errors
        for toolpack_id, errors in problems.items():
            for error in errors:
                print("FAIL {}: {}".format(toolpack_id, error))
        print("UBLADE3_TOOLPACKS count={} errors={}".format(len(manifests), len(problems)))
        print("UBLADE3_TOOLPACKS_OK" if not problems else "UBLADE3_TOOLPACKS_FAIL")
        return 0 if not problems else 1
    if args.toolpacks_command == "run":
        if args.toolpack_id not in manifests:
            print("FAIL unknown toolpack {!r}; have {}".format(args.toolpack_id, sorted(manifests)), file=sys.stderr)
            return 2
        manifest = manifests[args.toolpack_id]
        if args.action:
            result = toolpacks.run_action(manifest, args.action, project_root=REPO, state_dir=state_dir())
        else:
            result = toolpacks.run_toolpack(manifest, project_root=REPO, state_dir=state_dir())
        if args.json:
            print(json.dumps(result, indent=2))
        else:
            print("toolpack {}: passed={} artifacts={} validators={} state={}".format(
                result.get("toolpack"), result.get("passed"),
                len(result.get("artifacts", [])), len(result.get("validators", [])),
                result.get("statePath")))
            for error in result.get("errors", []):
                print("ERROR " + error)
        print("UBLADE3_TOOLPACK_OK toolpack={} passed=True state={}".format(
            manifest["id"], result.get("statePath")) if result.get("passed")
            else "UBLADE3_TOOLPACK_FAIL toolpack={} errors={}".format(manifest["id"], result.get("errors")))
        return 0 if result.get("passed") else 1
    return 2


def command_slice(home, args):
    slices = gypsy_module(home, "slices")
    orchestrator = gypsy_module(home, "orchestrator")
    manifests = slices.load_all(REPO / "slices")
    if args.slices_command == "list":
        for slice_id in sorted(manifests):
            manifest = manifests[slice_id]
            print("{}: {} (steps: {})".format(
                slice_id, manifest.get("description", ""),
                ", ".join(step["id"] for step in manifest.get("steps", []))))
        return 0
    if args.slices_command == "run":
        if args.slice_id not in manifests:
            print("FAIL unknown slice {!r}; have {}".format(args.slice_id, sorted(manifests)), file=sys.stderr)
            return 2
        result = orchestrator.run_slice(manifests[args.slice_id], project_root=REPO, state_dir=state_dir())
        if args.json:
            print(json.dumps(result, indent=2))
        else:
            print("slice {}: passed={} steps={} artifacts={} state={}".format(
                result.get("slice"), result.get("passed"), len(result.get("steps", [])),
                len(result.get("artifacts", [])), result.get("statePath")))
            for error in result.get("errors", []):
                print("ERROR " + error)
        print("UBLADE3_SLICE_OK slice={} passed=True state={}".format(args.slice_id, result.get("statePath")) if result.get("passed")
              else "UBLADE3_SLICE_FAIL slice={} errors={}".format(args.slice_id, result.get("errors")))
        return 0 if result.get("passed") else 1
    return 2



def _print_payload(payload):
    print(json.dumps(payload, indent=2, sort_keys=True))


def command_assets(home):
    module = gypsy_module(home, "assets")
    roots = [REPO / "unity/UBlade3/Assets", REPO / "docs", REPO / "scripts", REPO / "runtime"]
    payload = module.scan(roots=[str(p) for p in roots if p.exists()])
    _print_payload(payload)
    target = state_dir() / "assets" / "registry.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    module.save_state(payload, str(target))
    print("UBLADE3_GYPSY_ASSETS_OK files=" + str(len(payload.get("assets", []))))
    return 0


def command_metrics(home):
    module = gypsy_module(home, "metrics")
    payload = module.collect(state_dir=str(state_dir()))
    _print_payload(payload)
    module.write_metrics(str(state_dir() / "metrics.json"), payload)
    print("UBLADE3_GYPSY_METRICS_OK")
    return 0


def command_traces(home):
    module = gypsy_module(home, "traces")
    payload = module.collect(state_dir=str(state_dir()))
    _print_payload(payload)
    module.to_otel(payload)
    target = state_dir() / "traces.json"
    target.write_text(module.to_json(payload), encoding="utf-8")
    print("UBLADE3_GYPSY_TRACES_OK traces=" + str(len(payload.get("traces", []))))
    return 0


def command_provenance(home):
    module = gypsy_module(home, "provenance")
    candidates = [REPO / "unity/UBlade3/build/UBlade3.exe"]
    candidates += sorted((REPO / "artifacts/ublade3").glob("*.png"))
    candidates += [REPO / "artifacts/ublade3-release.json"]
    artifacts = [str(p.relative_to(REPO).as_posix()) for p in candidates if p.is_file()]
    if not artifacts:
        print("UBLADE3_GYPSY_PROVENANCE_FAIL no artifacts", file=sys.stderr)
        return 1
    document = module.create_provenance(artifacts, state_dir=str(state_dir()), project_root=str(REPO))
    target = REPO / "artifacts/ublade3-provenance.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    module.write_provenance(str(target), document)
    verification = module.verify_provenance(document, project_root=str(REPO), state_dir=str(state_dir()))
    sbom = module.build_sbom(str(REPO))
    sbom_path = REPO / "artifacts/ublade3-sbom.json"
    module.write_sbom(str(sbom_path), sbom)
    print("UBLADE3_GYPSY_PROVENANCE_OK artifacts=" + str(len(artifacts)) +
          " passed=" + str(verification.get("passed")) +
          " sbom_components=" + str(len(sbom.get("components", []))))
    return 0 if verification.get("passed") else 1


def main(argv=None):
    parser = argparse.ArgumentParser(description="Project-local Gypsy adapter for UBlade3.")
    parser.add_argument("--gypsy-home", help="Gypsy Studio checkout (default: GYPSY_HOME or the known path)")
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("check", help="run Gypsy's project check against this repository")
    sub.add_parser("capabilities", help="validate this repository's capability manifests with Gypsy's validator")
    sub.add_parser("assets", help="run Gypsy asset registry over the UBlade3 port assets")
    sub.add_parser("metrics", help="run Gypsy state-derived metrics")
    sub.add_parser("traces", help="run Gypsy state-derived traces")
    sub.add_parser("provenance", help="sign artifacts and write a deterministic SBOM with Gypsy")
    sub.add_parser("verify-provenance", help="verify the signed provenance document with Gypsy")

    toolpacks_parser = sub.add_parser("toolpacks")
    toolpack_commands = toolpacks_parser.add_subparsers(dest="toolpacks_command", required=True)
    toolpack_commands.add_parser("list")
    toolpack_commands.add_parser("check")
    toolpack_run = toolpack_commands.add_parser("run")
    toolpack_run.add_argument("toolpack_id")
    toolpack_run.add_argument("--action")
    toolpack_run.add_argument("--json", action="store_true")

    slice_parser = sub.add_parser("slice")
    slice_commands = slice_parser.add_subparsers(dest="slices_command", required=True)
    slice_commands.add_parser("list")
    slice_run = slice_commands.add_parser("run")
    slice_run.add_argument("slice_id")
    slice_run.add_argument("--json", action="store_true")

    args = parser.parse_args(argv)
    home = resolve_gypsy_home(args.gypsy_home)
    if not (home / "workstream" / "gypsy.py").is_file():
        print("FAIL Gypsy Studio not found at " + str(home), file=sys.stderr)
        return 2
    if args.command == "check":
        return command_check(home)
    if args.command == "capabilities":
        return command_capabilities(home)
    if args.command == "assets":
        return command_assets(home)
    if args.command == "metrics":
        return command_metrics(home)
    if args.command == "traces":
        return command_traces(home)
    if args.command == "provenance":
        return command_provenance(home)
    if args.command == "toolpacks":
        return command_toolpacks(home, args)
    if args.command == "slice":
        return command_slice(home, args)
    return 2


if __name__ == "__main__":
    sys.exit(main())
