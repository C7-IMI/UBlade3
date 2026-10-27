#!/usr/bin/env python3
"""Generate the UBlade3 port ledger.

Reads, read-only:
  third_party/hyperblade-decomp/src/**/*.{cpp,h}
  third_party/hyperblade-decomp/symbols/functions.csv   (when present)
  unity/UBlade3/Assets/Scripts/UBlade3/**/*.cs          (when present)

Writes:
  docs/PORT-LEDGER.json
  docs/PORT-LEDGER.md

Run from the repository root:

    python tools/port/generate_ledger.py
    python tools/port/generate_ledger.py --check     # exit 1 when the docs are stale

The tool is deterministic: no clock, no environment, no absolute paths and no
floating sort keys leak into the output. The decompilation submodule is only
ever opened for reading.

Mapping convention
------------------
A C# file claims a decompilation function by citing it in a comment or string,
either with the canonical address form (lower-case 0x plus eight hex digits) or
with the fully qualified decompilation symbol, for example:

    // decomp: src/engine/game.cpp Game::RunFrame HYPERX 0x0044c1b0

A citation in a file that also carries TODO, NotImplementedException or
NotSupportedException within STUB_WINDOW lines of the citation counts as a stub,
not as ported.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

TOOL_VERSION = "1.0.0"
SCHEMA_VERSION = 1

DEFAULT_SRC = "third_party/hyperblade-decomp/src"
DEFAULT_FUNCTIONS_CSV = "third_party/hyperblade-decomp/symbols/functions.csv"
DEFAULT_CSHARP = "unity/UBlade3/Assets/Scripts/UBlade3"
DEFAULT_CSHARP_FALLBACK = "unity/UBlade3/Assets/Scripts"
DEFAULT_JSON = "docs/PORT-LEDGER.json"
DEFAULT_MD = "docs/PORT-LEDGER.md"

# The decompilation README splits the image at 0x004a0000: below it is game code,
# above it is the C runtime and the libraries the original linked.
GAME_CODE_LIMIT = 0x004A0000

STUB_WINDOW = 40
DEFAULT_TOP = 50

ADDRESS_RE = re.compile(r"0x([0-9a-fA-F]{8})")
FUNCTION_MARKER_RE = re.compile(r"^\s*//\s*FUNCTION:\s*HYPERX\s+0x([0-9a-fA-F]{8})\b")
SYNTHETIC_MARKER_RE = re.compile(r"^\s*//\s*SYNTHETIC:\s*HYPERX\s+0x([0-9a-fA-F]{8})\b")
GLOBAL_MARKER_RE = re.compile(r"^\s*//\s*GLOBAL:\s*HYPERX\s+0x([0-9a-fA-F]{8})\b")
VTABLE_MARKER_RE = re.compile(r"^\s*//\s*VTABLE:\s*HYPERX\s+0x([0-9a-fA-F]{8})\b")
STRING_MARKER_RE = re.compile(r"^\s*//\s*STRING:\s*HYPERX\s+0x([0-9a-fA-F]{8})\b")
CLASS_RE = re.compile(r"^\s*(class|struct)\s+([A-Za-z_]\w*)\b")
CS_CLASS_RE = re.compile(r"\b(class|struct|record|interface)\s+([A-Za-z_]\w*)")
CS_METHOD_RE = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal)\s+"
    r"(?:static\s+|virtual\s+|override\s+|sealed\s+|async\s+|new\s+|unsafe\s+|extern\s+|partial\s+)*"
    r"[\w<>\[\],\.\?]+\s+([A-Za-z_]\w*)\s*\("
)
STUB_RE = re.compile(r"\bTODO\b|NotImplementedException|NotSupportedException")
DECOMP_PATH_RE = re.compile(r"(src/[a-z]+/[a-z0-9_]+\.(?:cpp|h))")
DECOMP_NAME_RE = re.compile(r"\b[A-Za-z_]\w*::[A-Za-z_]\w*\b")

SUBSYSTEMS = [
    "anim", "app", "common", "engine", "graphics", "input", "jpeg", "math",
    "net", "physics", "planvm", "sound", "ui",
]

SUBSYSTEM_WEIGHT = {
    "engine": 100,
    "physics": 95,
    "input": 92,
    "planvm": 70,
    "common": 65,
    "math": 60,
    "anim": 40,
    "app": 35,
    "graphics": 15,
    "sound": 10,
    "ui": 8,
    "net": 5,
    "jpeg": 1,
    "unknown": 0,
}

# Source files whose functions come first in a first-playable port.
CORE_FILE_BONUS = {
    "engine/game.cpp": 80,
    "engine/gameengine.cpp": 60,
    "engine/arenaellipsoidstatic.cpp": 90,
    "engine/cameraobject.cpp": 70,
    "engine/cameraprojectionstate.cpp": 55,
    "engine/ctrack.cpp": 65,
    "engine/rokobject.cpp": 65,
    "engine/objectstats.cpp": 85,
    "engine/player.cpp": 85,
    "engine/engineobject.cpp": 75,
    "engine/objectdef.cpp": 70,
    "physics/collisionellipsoid.cpp": 110,
    "physics/physicsbody.cpp": 110,
    "physics/collisioncheck.cpp": 70,
    "physics/collisionplane.cpp": 60,
    "planvm/cglideonsurface.cpp": 105,
    "planvm/cmonitorkeys.cpp": 100,
    "planvm/cspeedburst.cpp": 90,
    "planvm/cprojectandbounce.cpp": 95,
    "planvm/planactionlaunch.cpp": 85,
    "planvm/plancond.cpp": 80,
    "planvm/cmonitorheldkeys.cpp": 75,
    "planvm/cupdateshotpower.cpp": 65,
    "planvm/csteertotarget.cpp": 60,
    "planvm/csteertoward.cpp": 55,
    "input/keyboarddeviceobj.cpp": 95,
    "input/actionbindings.cpp": 85,
    "input/joystickmgr.cpp": 45,
    "math/vec3.cpp": 70,
    "math/math.cpp": 65,
    "math/transformobject.cpp": 60,
    "ui/infobar.cpp": 55,
}

# Name fragments worth points in the roadmap. (fragment, weight, reason)
NAME_PRIORITY: Sequence[Tuple[str, int, str]] = (
    ("CGlideOnSurface::Process", 100, "skater glide step"),
    ("CMonitorKeys::Process", 95, "input to physics step"),
    ("CProjectAndBounce::ProjectAndBounce", 95, "rok flight step"),
    ("PlanAction::Launch", 85, "rok launch"),
    ("PlanAction::Throw", 85, "rok throw"),
    ("PhysicsBody::InitOnSurface", 80, "surface projection"),
    ("PhysicsBody::TakeSurfaceNormal", 75, "surface normal"),
    ("ObjectStats::InitDefaults", 75, "rating defaults"),
    ("ObjectStats::ScaleTo", 70, "difficulty scaling"),
    ("CProjectAndBounce::IntegrateSpringAndGravity", 75, "rok gravity"),
    ("Cmd_SetMotionParams", 85, "data loader"),
    ("Cmd_SetRatings", 75, "data loader"),
    ("LoadObjectsFromConfig", 70, "data loader"),
    ("RunFrame", 120, "frame loop"),
    ("TickClock", 120, "clock"),
    ("PerFrameUpdate", 115, "frame update"),
    ("InitMatchTimers", 110, "clock init"),
    ("BeginMatchPlay", 100, "match start"),
    ("RestartMatch", 90, "match restart"),
    ("UpdateState", 55, "state machine"),
    ("CollisionEllipsoid::", 70, "arena shell"),
    ("IntersectRay", 95, "ray/ellipsoid"),
    ("IntersectSegment", 85, "ray/ellipsoid"),
    ("SegmentIntersection", 80, "ray/ellipsoid"),
    ("NormalAtPoint", 95, "arena normal"),
    ("PointInside", 85, "arena shell"),
    ("PointNearSurface", 70, "arena shell"),
    ("ComputeRadialBoundaryXY", 75, "arena boundary"),
    ("RadiusForDirection", 65, "arena radius"),
    ("GetZ", 70, "arena height"),
    ("UpdateMovementAxes", 105, "steering input"),
    ("ApplySteeringForce", 105, "steering"),
    ("IntegrateMotion", 95, "motion integration"),
    ("PreUpdateTick", 95, "tick scaling"),
    ("PerFrameTick", 90, "tick"),
    ("UpdateVelocityDirection", 90, "surface response"),
    ("BindToObject", 90, "body init"),
    ("ResetOnSurface", 85, "surface reset"),
    ("ResetDynamics", 70, "body reset"),
    ("SetFlatOnTerrain", 75, "terrain pose"),
    ("StepWithGravity", 75, "gravity step"),
    ("IntegrateGravity", 75, "gravity"),
    ("UpdateBanking", 65, "banking"),
    ("ApplyBankForce", 55, "banking"),
    ("CameraObject", 40, "camera"),
    ("ViewIt", 75, "camera view"),
    ("UpdateSmoothing", 70, "camera smoothing"),
    ("SetPosHistorySize", 65, "camera window"),
    ("SetDirHistorySize", 65, "camera window"),
    ("CameraProjectionState", 45, "projection"),
    ("CTrack", 55, "camera tracking"),
    ("CGlideOnSurface", 60, "skater glide"),
    ("CMonitorKeys", 60, "input to physics"),
    ("CMonitorHeldKeys", 80, "held keys"),
    ("CSpeedBurst", 55, "speed burst"),
    ("CProjectAndBounce", 60, "rok flight"),
    ("CUpdateShotPower", 65, "shot power"),
    ("CRelaxYaw", 50, "yaw relax"),
    ("RokObject", 50, "rok"),
    ("RokScoreValue", 105, "goal value"),
    ("IncRokValue", 100, "multiplier"),
    ("RokIsCharged", 70, "multiplier"),
    ("GoThroughMuliplier", 80, "multiplier AI"),
    ("IncrementScore", 90, "score"),
    ("KeyboardDeviceObj", 60, "keyboard"),
    ("BuildActionBindingTable", 95, "bindings"),
    ("HandleKeyDown", 85, "key down"),
    ("HandleKeyUp", 85, "key up"),
    ("CommitKeyBindings", 85, "bindings"),
    ("CheckButtonTimeouts", 80, "held keys"),
    ("LoadBindingsFromIni", 65, "bindings"),
    ("ObjectStats", 55, "ratings"),
    ("GetWeightedStat", 85, "ratings"),
    ("UpdateMomentumAndEnergy", 75, "energy"),
    ("GetScalar", 65, "ratings"),
    ("Set_SpeedBurst", 85, "speed burst"),
    ("LaunchToward", 80, "rok launch"),
    ("LaunchAt", 70, "rok launch"),
    ("Throw", 80, "rok throw"),
    ("KickDownToSurface", 50, "rok surface"),
    ("CGlideOnSurface::Bind", 70, "skater glide setup"),
    ("CMonitorKeys::Bind", 75, "input to physics setup"),
    ("CProjectAndBounce::Bind", 65, "rok flight setup"),
    ("CSpeedBurst::Bind", 65, "speed burst setup"),
    ("KeyboardDeviceObj::OnButtonDown", 55, "keyboard button"),
    ("ParseTeamHTMFile", 70, "data loader"),
    ("ParseObjectDefinition", 65, "data loader"),
    ("Cmd_SetCollisionObject", 65, "data loader"),
    ("Cmd_DefineObject", 60, "data loader"),
    ("GameEngine::", 15, "engine"),
    ("Game::", 20, "engine"),
    ("InitMatchMarkers", 80, "arena markers"),
    ("ShowRokValue", 60, "multiplier display"),
)

ACCESSOR_RE = re.compile(r"^(Get|Set|Is|Resize|Sort)[A-Z]")
BOOKKEEPING_NAMES = ("OnListChanged", "Postpass", "ProcessReceivedPacket")

NAME_PENALTY = (
    ("DirectPlay", 120), ("GameSession", 110), ("Network", 70), ("Session", 40),
    ("Packet", 60), ("Bitmap", 50), ("Font", 40), ("Palette", 40),
    ("Dialog", 50), ("Widget", 50), ("Screen", 30), ("Sound", 35),
    ("Music", 35), ("Joystick", 30), ("Cheat", 20), ("Debug", 25),
    ("Trace", 30), ("Fatal", 40), ("Error", 25), ("JPEG", 80), ("D3D", 50),
    ("DDraw", 50), ("Texture", 40), ("Renderer", 40), ("Viewport", 40),
    ("Bmp", 50), ("Save", 25), ("Load", 15), ("String::", 25), ("Mem", 30),
    ("Unload", 40), ("InitAgendaItemPools", 60), ("CreateAgendaItemPools", 60),
    ("DestroyAgendaItemPools", 50), ("RegisterObject", 40), ("UnregisterObject", 40),
    ("InsertObjectSorted", 40), ("SyncRenderQueueSize", 40), ("InitializeEngine", 50),
    ("ShutdownEngine", 50), ("TogglePause", 35), ("AdvanceTournamentBracket", 40),
    ("IsFitSubstitute", 40), ("SubstitutePlayer", 30), ("CountEntitiesInRange", 20),
)


def rel(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        print("warning: cannot read {}: {}".format(path, exc), file=sys.stderr)
        return ""


def canonical_address(value: str) -> str:
    return "0x{:08x}".format(int(value, 16))


def subsystem_of(relpath: str) -> str:
    parts = relpath.split("/")
    if len(parts) > 1 and parts[0] in SUBSYSTEMS:
        return parts[0]
    return "unknown"


def load_functions(csv_path: Path) -> Tuple[List[dict], bool]:
    rows: List[dict] = []
    if not csv_path.is_file():
        return rows, False
    # csv module: quoted signatures contain commas.
    import csv as csv_module

    with csv_path.open("r", encoding="utf-8", errors="replace", newline="") as handle:
        reader = csv_module.reader(handle)
        header: Optional[List[str]] = None
        for raw in reader:
            if not raw:
                continue
            first = raw[0].strip()
            if header is None:
                if first.lower() == "address":
                    header = [cell.strip().lower() for cell in raw]
                    continue
                header = ["address", "name", "size", "convention", "signature"]
            if first.startswith("#"):
                continue
            fields = list(raw) + [""] * (5 - len(raw))
            address_text = fields[0].strip()
            if not re.fullmatch(r"0x[0-9a-fA-F]{1,8}", address_text):
                continue
            address = canonical_address(address_text)
            size_text = fields[2].strip() or "0"
            try:
                size = int(size_text, 0)
            except ValueError:
                size = 0
            rows.append({
                "address": address,
                "addressValue": int(address, 16),
                "name": fields[1].strip(),
                "size": size,
                "convention": fields[3].strip(),
                "signature": fields[4].strip(),
            })
    rows.sort(key=lambda row: row["addressValue"])
    return rows, True


def scan_sources(src_root: Path, repo_root: Path) -> Tuple[List[dict], List[dict], Dict[str, dict]]:
    files: List[dict] = []
    classes: List[dict] = []
    marker_index: Dict[str, dict] = {}
    if not src_root.is_dir():
        return files, classes, marker_index
    paths = sorted(
        (path for path in src_root.rglob("*")
         if path.is_file() and path.suffix.lower() in (".cpp", ".h")),
        key=lambda path: rel(path, repo_root),
    )
    prefix = DEFAULT_SRC + "/"
    for path in paths:
        relpath = rel(path, repo_root)
        text = read_text(path)
        lines = text.splitlines()
        subsystem = subsystem_of(relpath[len(prefix):]) if relpath.startswith(prefix) else "unknown"
        kind = "cpp" if path.suffix.lower() == ".cpp" else "header"
        marker_count = 0
        synthetic_count = 0
        global_count = 0
        vtable_count = 0
        string_count = 0
        class_count = 0
        first_marker_line: Optional[int] = None
        for index, line in enumerate(lines, start=1):
            marker = FUNCTION_MARKER_RE.match(line)
            if marker:
                marker_count += 1
                address = canonical_address(marker.group(1))
                if first_marker_line is None:
                    first_marker_line = index
                if address not in marker_index:
                    marker_index[address] = {
                        "address": address,
                        "sourceFile": relpath,
                        "sourceLine": index,
                        "subsystem": subsystem,
                    }
                continue
            if SYNTHETIC_MARKER_RE.match(line):
                synthetic_count += 1
            elif GLOBAL_MARKER_RE.match(line):
                global_count += 1
            elif VTABLE_MARKER_RE.match(line):
                vtable_count += 1
            elif STRING_MARKER_RE.match(line):
                string_count += 1
            else:
                class_match = CLASS_RE.match(line)
                if class_match:
                    class_count += 1
                    classes.append({
                        "name": class_match.group(2),
                        "kind": class_match.group(1),
                        "path": relpath,
                        "line": index,
                        "subsystem": subsystem,
                        "forwardDeclaration": line.rstrip().endswith(";"),
                    })
        files.append({
            "path": relpath,
            "subsystem": subsystem,
            "kind": kind,
            "bytes": path.stat().st_size,
            "lines": len(lines),
            "functionMarkers": marker_count,
            "syntheticMarkers": synthetic_count,
            "globalMarkers": global_count,
            "vtableMarkers": vtable_count,
            "stringMarkers": string_count,
            "classDeclarations": class_count,
            "firstMarkerLine": first_marker_line,
        })
    files.sort(key=lambda item: item["path"])
    classes.sort(key=lambda item: (item["path"], item["line"], item["name"]))
    return files, classes, marker_index


def scan_csharp(cs_root: Path, repo_root: Path) -> List[dict]:
    files: List[dict] = []
    if not cs_root.is_dir():
        return files
    paths = sorted((path for path in cs_root.rglob("*.cs") if path.is_file()),
                   key=lambda path: rel(path, repo_root))
    for path in paths:
        text = read_text(path)
        lines = text.splitlines()
        addresses: Dict[str, List[int]] = {}
        names: Dict[str, List[int]] = {}
        decomp_paths: Dict[str, List[int]] = {}
        for index, line in enumerate(lines, start=1):
            for match in ADDRESS_RE.finditer(line):
                address = canonical_address(match.group(1))
                addresses.setdefault(address, []).append(index)
            for match in DECOMP_PATH_RE.finditer(line):
                decomp_paths.setdefault(match.group(1), []).append(index)
            for match in DECOMP_NAME_RE.finditer(line):
                names.setdefault(match.group(0), []).append(index)
        class_names = CS_CLASS_RE.findall(text)
        method_count = 0
        for line in lines:
            if CS_METHOD_RE.match(line):
                method_count += 1
        files.append({
            "path": rel(path, repo_root),
            "lines": len(lines),
            "bytes": path.stat().st_size,
            "classes": len(class_names),
            "classNames": sorted({name for _, name in class_names}),
            "methods": method_count,
            "addressCitations": {key: sorted(set(value)) for key, value in sorted(addresses.items())},
            "decompPathCitations": {key: sorted(set(value)) for key, value in sorted(decomp_paths.items())},
            "decompNameCitations": {key: sorted(set(value)) for key, value in sorted(names.items())},
            "text": text,
        })
    return files


def classify_functions(rows: List[dict], marker_index: Dict[str, dict]) -> List[dict]:
    inventory: List[dict] = []
    for row in rows:
        marker = marker_index.get(row["address"])
        inventory.append({
            "address": row["address"],
            "name": row["name"],
            "size": row["size"],
            "convention": row["convention"],
            "signature": row["signature"],
            "sourceFile": marker["sourceFile"] if marker else "",
            "sourceLine": marker["sourceLine"] if marker else 0,
            "subsystem": marker["subsystem"] if marker else "unknown",
            "gameFunction": row["addressValue"] < GAME_CODE_LIMIT,
        })
    return inventory


def find_mappings(inventory: List[dict], csharp_files: List[dict]) -> Tuple[List[dict], List[dict], List[dict]]:
    known_names = {item["name"] for item in inventory if "::" in item["name"]}
    by_address: Dict[str, List[Tuple[str, int, List[str]]]] = {}
    by_name: Dict[str, List[Tuple[str, int, List[str]]]] = {}
    for cs in csharp_files:
        window = cs["text"].splitlines()
        for address, citation_lines in cs["addressCitations"].items():
            for line in citation_lines:
                context = window[max(0, line - 1 - STUB_WINDOW): line + STUB_WINDOW]
                by_address.setdefault(address, []).append((cs["path"], line, context))
        for name, citation_lines in cs["decompNameCitations"].items():
            if name not in known_names:
                continue
            for line in citation_lines:
                context = window[max(0, line - 1 - STUB_WINDOW): line + STUB_WINDOW]
                by_name.setdefault(name, []).append((cs["path"], line, context))

    mapped: List[dict] = []
    stubs: List[dict] = []
    unmapped: List[dict] = []
    for function in inventory:
        citations = by_address.get(function["address"], [])
        if not citations:
            citations = by_name.get(function["name"], [])
        if citations:
            citations.sort(key=lambda item: (item[0], item[1]))
            path, line, context = citations[0]
            is_stub = any(STUB_RE.search(text) for text in context)
            record = dict(function)
            record["csharpFile"] = path
            record["csharpLine"] = line
            record["status"] = "stub" if is_stub else "ported"
            mapped.append(record)
            if is_stub:
                stubs.append(record)
        else:
            record = dict(function)
            record["status"] = "unmapped"
            unmapped.append(record)
    return mapped, stubs, unmapped


def infer_subsystems(inventory: List[dict], classes: List[dict]) -> int:
    counts: Dict[str, Dict[str, int]] = {}
    for item in inventory:
        if not item["sourceFile"] or "::" not in item["name"]:
            continue
        prefix = item["name"].split("::", 1)[0]
        bucket = counts.setdefault(prefix, {})
        bucket[item["subsystem"]] = bucket.get(item["subsystem"], 0) + 1
    for cls in classes:
        bucket = counts.setdefault(cls["name"], {})
        bucket[cls["subsystem"]] = bucket.get(cls["subsystem"], 0) + 1
    order = {name: index for index, name in enumerate(SUBSYSTEMS + ["unknown"])}
    resolved: Dict[str, str] = {}
    for prefix, bucket in counts.items():
        resolved[prefix] = sorted(bucket, key=lambda name: (-bucket[name], order.get(name, 99)))[0]
    inferred = 0
    for item in inventory:
        if item["sourceFile"] or "::" not in item["name"]:
            continue
        prefix = item["name"].split("::", 1)[0]
        if prefix in resolved:
            item["subsystem"] = resolved[prefix]
            item["subsystemInferred"] = True
            inferred += 1
    return inferred


def priority_of(function: dict) -> Tuple[int, List[str]]:
    score = 0
    reasons: List[str] = []
    subsystem = function["subsystem"]
    score += SUBSYSTEM_WEIGHT.get(subsystem, 0)
    if subsystem != "unknown":
        reasons.append("subsystem " + subsystem)
    source_file = function["sourceFile"]
    for fragment, bonus in CORE_FILE_BONUS.items():
        if source_file.endswith(fragment):
            score += bonus
            reasons.append("core file " + fragment)
            break
    name = function["name"]
    for fragment, bonus, reason in NAME_PRIORITY:
        if fragment in name:
            score += bonus
            reasons.append(reason)
            break
    leaf = name.split("::")[-1] if "::" in name else name
    if ACCESSOR_RE.match(leaf) and function["size"] < 200:
        score -= 60
        reasons.append("-accessor")
    elif leaf.startswith("~"):
        score -= 45
        reasons.append("-destructor")
    elif leaf in BOOKKEEPING_NAMES:
        score -= 30
        reasons.append("-bookkeeping")
    for fragment, penalty in NAME_PENALTY:
        if fragment in name:
            score -= penalty
            reasons.append("-" + fragment)
    return score, reasons


def build_next_functions(unmapped: List[dict], top: int) -> List[dict]:
    candidates = [function for function in unmapped if function["gameFunction"]]
    ranked: List[Tuple[int, int, dict, List[str]]] = []
    for function in candidates:
        score, reasons = priority_of(function)
        ranked.append((score, int(function["address"], 16), function, reasons))
    ranked.sort(key=lambda item: (-item[0], item[1]))
    result: List[dict] = []
    for index, (score, _address, function, reasons) in enumerate(ranked[:top], start=1):
        result.append({
            "rank": index,
            "address": function["address"],
            "name": function["name"],
            "size": function["size"],
            "subsystem": function["subsystem"],
            "sourceFile": function["sourceFile"],
            "sourceLine": function["sourceLine"],
            "priority": score,
            "reasons": reasons,
        })
    return result


def summarise(repo_root: Path, src_root: Path, functions_csv: Path, csharp_root: Path) -> dict:
    functions, csv_present = load_functions(functions_csv)
    source_files, classes, marker_index = scan_sources(src_root, repo_root)
    csharp_root_present = csharp_root.is_dir()
    if csharp_root_present:
        csharp_files = scan_csharp(csharp_root, repo_root)
        csharp_root_used = csharp_root
    else:
        fallback = repo_root / DEFAULT_CSHARP_FALLBACK
        fallback_files = scan_csharp(fallback, repo_root) if fallback.is_dir() else []
        csharp_files = fallback_files
        csharp_root_used = fallback if fallback_files else csharp_root

    inventory = classify_functions(functions, marker_index)
    if not csv_present:
        for address, marker in sorted(marker_index.items()):
            inventory.append({
                "address": address,
                "name": "",
                "size": 0,
                "convention": "",
                "signature": "",
                "sourceFile": marker["sourceFile"],
                "sourceLine": marker["sourceLine"],
                "subsystem": marker["subsystem"],
                "gameFunction": int(address, 16) < GAME_CODE_LIMIT,
            })
        inventory.sort(key=lambda item: int(item["address"], 16))

    inferred_subsystems = infer_subsystems(inventory, classes)
    mapped, stubs, unmapped = find_mappings(inventory, csharp_files)

    for source in source_files:
        source["inventoryFunctions"] = sum(
            1 for item in inventory if item["sourceFile"] == source["path"])
        source["mappedFunctions"] = sum(
            1 for item in mapped if item["sourceFile"] == source["path"])
        source["stubFunctions"] = sum(
            1 for item in stubs if item["sourceFile"] == source["path"])
        source["unmappedFunctions"] = sum(
            1 for item in unmapped if item["sourceFile"] == source["path"])

    subsystems: List[dict] = []
    for subsystem in SUBSYSTEMS + ["unknown"]:
        files = [item for item in source_files if item["subsystem"] == subsystem]
        if not files:
            continue
        system_functions = [item for item in inventory if item["subsystem"] == subsystem]
        system_mapped = [item for item in mapped if item["subsystem"] == subsystem]
        system_stubs = [item for item in stubs if item["subsystem"] == subsystem]
        subsystems.append({
            "name": subsystem,
            "cppFiles": sum(1 for item in files if item["kind"] == "cpp"),
            "headerFiles": sum(1 for item in files if item["kind"] == "header"),
            "lines": sum(item["lines"] for item in files),
            "functions": len(system_functions),
            "mapped": len(system_mapped),
            "stubs": len(system_stubs),
            "unmapped": len(system_functions) - len(system_mapped),
            "coveragePercent": round(100.0 * len(system_mapped) / len(system_functions), 2)
            if system_functions else 0.0,
        })

    game_functions = [item for item in inventory if item["gameFunction"]]
    game_mapped = [item for item in mapped if item["gameFunction"]]
    citation_count = sum(len(lines) for cs in csharp_files
                         for lines in cs["addressCitations"].values())
    unique_citations = sorted({address for cs in csharp_files for address in cs["addressCitations"]})
    known_addresses = {item["address"] for item in inventory}
    unknown_citations = [address for address in unique_citations if address not in known_addresses]

    next_functions = build_next_functions(unmapped, DEFAULT_TOP)
    warnings: List[str] = []
    if not csv_present:
        warnings.append("symbols/functions.csv is absent; inventory is the source FUNCTION markers only")
    if not csharp_root_present:
        warnings.append(
            "C# root {} is absent; coverage is reported as zero and every function is unmapped".format(
                rel(csharp_root, repo_root)))
    elif csharp_root_used != csharp_root:
        warnings.append(
            "C# root {} is absent; fell back to {}".format(
                rel(csharp_root, repo_root), rel(csharp_root_used, repo_root)))
    if unknown_citations:
        warnings.append("{} C# address citations name no known decompilation function".format(
            len(unknown_citations)))
    markerless = [item for item in inventory if not item["sourceFile"]]
    if markerless:
        warnings.append(
            "{} functions have no FUNCTION marker in src (unwritten in the decompilation); "
            "{} of them have a subsystem inferred from their class name and their source file "
            "stays unknown".format(len(markerless), inferred_subsystems))

    totals = {
        "sourceFiles": len(source_files),
        "sourceCppFiles": sum(1 for item in source_files if item["kind"] == "cpp"),
        "sourceHeaderFiles": sum(1 for item in source_files if item["kind"] == "header"),
        "sourceLines": sum(item["lines"] for item in source_files),
        "functionMarkers": sum(item["functionMarkers"] for item in source_files),
        "syntheticMarkers": sum(item["syntheticMarkers"] for item in source_files),
        "globalMarkers": sum(item["globalMarkers"] for item in source_files),
        "vtableMarkers": sum(item["vtableMarkers"] for item in source_files),
        "stringMarkers": sum(item["stringMarkers"] for item in source_files),
        "classDefinitions": len([item for item in classes if not item["forwardDeclaration"]]),
        "classDeclarations": len(classes),
        "functionsCsvRows": len(functions),
        "inventoryFunctions": len(inventory),
        "gameFunctions": len(game_functions),
        "libraryFunctions": len(inventory) - len(game_functions),
        "mappedFunctions": len(mapped),
        "portedFunctions": len(mapped) - len(stubs),
        "stubFunctions": len(stubs),
        "unmappedFunctions": len(unmapped),
        "mappedGameFunctions": len(game_mapped),
        "unmappedGameFunctions": len(game_functions) - len(game_mapped),
        "coveragePercent": round(100.0 * len(mapped) / len(inventory), 2) if inventory else 0.0,
        "gameCoveragePercent": round(100.0 * len(game_mapped) / len(game_functions), 2)
        if game_functions else 0.0,
        "csharpFiles": len(csharp_files),
        "csharpLines": sum(cs["lines"] for cs in csharp_files),
        "csharpClasses": sum(cs["classes"] for cs in csharp_files),
        "csharpMethods": sum(cs["methods"] for cs in csharp_files),
        "csharpAddressCitations": citation_count,
        "csharpFilesWithCitations": sum(1 for cs in csharp_files if cs["addressCitations"]),
        "nextFunctionCount": len(next_functions),
    }

    return {
        "schemaVersion": SCHEMA_VERSION,
        "tool": {
            "name": "generate_ledger",
            "version": TOOL_VERSION,
            "path": "tools/port/generate_ledger.py",
            "mappingRule": (
                "Cite a decompilation function by canonical address (0x plus eight hex digits) "
                "or by its fully qualified decompilation symbol; TODO/NotImplementedException "
                "within {} lines marks a stub.".format(STUB_WINDOW)),
        },
        "inputs": {
            "decompSrcRoot": DEFAULT_SRC,
            "decompSrcPresent": src_root.is_dir(),
            "functionsCsv": DEFAULT_FUNCTIONS_CSV,
            "functionsCsvPresent": csv_present,
            "csharpRoot": rel(csharp_root, repo_root),
            "csharpRootPresent": csharp_root_present,
            "csharpRootUsed": rel(csharp_root_used, repo_root),
        },
        "totals": totals,
        "subsystems": subsystems,
        "sourceFiles": source_files,
        "classes": classes,
        "functions": inventory,
        "coverage": {
            "mapped": mapped,
            "stubs": stubs,
            "unmapped": unmapped,
        },
        "csharp": {
            "root": rel(csharp_root_used, repo_root),
            "files": [
                {
                    "path": cs["path"],
                    "lines": cs["lines"],
                    "classes": cs["classes"],
                    "classNames": cs["classNames"],
                    "methods": cs["methods"],
                    "addressCitationCount": sum(
                        len(lines) for lines in cs["addressCitations"].values()),
                    "addressCitations": cs["addressCitations"],
                    "decompPathCitations": cs["decompPathCitations"],
                }
                for cs in csharp_files
            ],
        },
        "nextFunctions": next_functions,
        "warnings": warnings,
    }


def render_markdown(ledger: dict) -> str:
    totals = ledger["totals"]
    lines: List[str] = []
    add = lines.append
    add("# UBlade3 port ledger")
    add("")
    add("Generated by tools/port/generate_ledger.py (schema {}). Do not edit this file by hand;".format(
        ledger["schemaVersion"]))
    add("rerun the tool after any change to the decompilation submodule or the Unity port.")
    add("")
    add("Source of truth: {} (the whole decompilation, C runtime included) against the Unity C#".format(
        ledger["inputs"]["decompSrcRoot"]))
    add("port under {}.".format(ledger["inputs"]["csharpRootUsed"]))
    add("")
    add("## Totals")
    add("")
    add("| Measure | Value |")
    add("| --- | ---: |")
    add("| Decompilation source files (cpp/h) | {:,} |".format(totals["sourceFiles"]))
    add("| Decompilation source lines | {:,} |".format(totals["sourceLines"]))
    add("| Class/struct declarations | {:,} |".format(totals["classDefinitions"]))
    add("| FUNCTION markers in source | {:,} |".format(totals["functionMarkers"]))
    add("| Rows in symbols/functions.csv | {:,} |".format(totals["functionsCsvRows"]))
    add("| Inventory functions (CSV or markers) | {:,} |".format(totals["inventoryFunctions"]))
    add("| Game functions below 0x004a0000 | {:,} |".format(totals["gameFunctions"]))
    add("| Library/CRT functions | {:,} |".format(totals["libraryFunctions"]))
    add("| Mapped C# functions | {:,} |".format(totals["mappedFunctions"]))
    add("| Mapped as stubs (TODO/NotImplemented) | {:,} |".format(totals["stubFunctions"]))
    add("| Mapped as ported | {:,} |".format(totals["portedFunctions"]))
    add("| Unmapped functions | {:,} |".format(totals["unmappedFunctions"]))
    add("| Whole-image coverage | {:.2f}% |".format(totals["coveragePercent"]))
    add("| Game-code coverage | {:.2f}% |".format(totals["gameCoveragePercent"]))
    add("| C# port files | {:,} |".format(totals["csharpFiles"]))
    add("| C# port lines | {:,} |".format(totals["csharpLines"]))
    add("| C# address citations | {:,} |".format(totals["csharpAddressCitations"]))
    add("")
    add("## Per-subsystem")
    add("")
    add("| Subsystem | cpp | h | Lines | Functions | Mapped | Stubs | Unmapped | Coverage |")
    add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    for subsystem in ledger["subsystems"]:
        add("| {} | {:,} | {:,} | {:,} | {:,} | {:,} | {:,} | {:,} | {:.2f}% |".format(
            subsystem["name"], subsystem["cppFiles"], subsystem["headerFiles"],
            subsystem["lines"], subsystem["functions"], subsystem["mapped"],
            subsystem["stubs"], subsystem["unmapped"], subsystem["coveragePercent"]))
    add("")
    add("## C# coverage")
    add("")
    if not ledger["csharp"]["files"]:
        add("No C# files exist yet; every decompilation function is unmapped. Create files under")
        add("{} and cite the decompilation function in a comment, for example:".format(
            ledger["inputs"]["csharpRoot"]))
        add("")
        add("    // decomp: src/engine/game.cpp Game::RunFrame HYPERX 0x0044c1b0")
        add("")
    else:
        add("| C# file | Lines | Classes | Methods | Address citations |")
        add("| --- | ---: | ---: | ---: | ---: |")
        for cs in ledger["csharp"]["files"]:
            add("| {} | {:,} | {:,} | {:,} | {:,} |".format(
                cs["path"], cs["lines"], cs["classes"], cs["methods"],
                cs["addressCitationCount"]))
        add("")
        if ledger["coverage"]["mapped"]:
            add("### Mapped functions")
            add("")
            add("| Address | Function | Status | C# file |")
            add("| --- | --- | --- | --- |")
            for item in ledger["coverage"]["mapped"]:
                add("| {} | {} | {} | {}:{} |".format(
                    item["address"], item["name"] or "(marker only)", item["status"],
                    item["csharpFile"], item["csharpLine"]))
            add("")
    add("## Next {} functions to port (core-gameplay priority)".format(
        totals["nextFunctionCount"]))
    add("")
    add("Deterministic priority: game functions below 0x004a0000 that no C# file cites yet,")
    add("scored by subsystem, core source file and core-system name, then ordered by address.")
    add("")
    add("| # | Address | Function | Subsystem | Source | Why |")
    add("| ---: | --- | --- | --- | --- | --- |")
    for item in ledger["nextFunctions"]:
        source = "{}:{}".format(item["sourceFile"], item["sourceLine"]) if item["sourceFile"] else ""
        reasons = ", ".join(item["reasons"][:4]) if item["reasons"] else ""
        add("| {} | {} | {} | {} | {} | {} |".format(
            item["rank"], item["address"], item["name"] or "(marker only)",
            item["subsystem"], source, reasons))
    add("")
    add("## Per-source-file counts")
    add("")
    add("| Source file | Kind | Lines | FUNCTION | Mapped | Stubs | Unmapped | Classes |")
    add("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |")
    for source in ledger["sourceFiles"]:
        add("| {} | {} | {:,} | {:,} | {:,} | {:,} | {:,} | {:,} |".format(
            source["path"], source["kind"], source["lines"], source["functionMarkers"],
            source["mappedFunctions"], source["stubFunctions"], source["unmappedFunctions"],
            source["classDeclarations"]))
    add("")
    add("## Class inventory")
    add("")
    add("{} class/struct declarations across the decompilation source. The complete list with")
    add("file and line is in docs/PORT-LEDGER.json under classes.".format(totals["classDefinitions"]))
    add("")
    add("| Subsystem | Class/struct declarations |")
    add("| --- | ---: |")
    class_counts: Dict[str, int] = {}
    for item in ledger["classes"]:
        if item["forwardDeclaration"]:
            continue
        class_counts[item["subsystem"]] = class_counts.get(item["subsystem"], 0) + 1
    for subsystem in SUBSYSTEMS + ["unknown"]:
        if subsystem in class_counts:
            add("| {} | {:,} |".format(subsystem, class_counts[subsystem]))
    add("")
    add("## Notes and limits")
    add("")
    for warning in ledger["warnings"]:
        add("- " + warning)
    if not ledger["warnings"]:
        add("- No input warnings.")
    add("- Coverage counts only functions cited by C#; a citation is not a parity test.")
    add("- symbols/functions.csv names functions the original linked, the C runtime included;")
    add("  the game-code split at 0x004a0000 follows the decompilation README.")
    add("- The tool never writes inside third_party/; the submodule is opened read-only.")
    add("")
    return "\n".join(lines)


def write_if_changed(path: Path, content: str) -> bool:
    old: Optional[str] = None
    if path.is_file():
        try:
            old = path.read_text(encoding="utf-8")
        except OSError:
            old = None
    if old == content:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)
    return True


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="Generate docs/PORT-LEDGER.json and docs/PORT-LEDGER.md for UBlade3.")
    parser.add_argument("--repo-root", default=None,
                        help="repository root; defaults to the tool's checkout")
    parser.add_argument("--json", default=DEFAULT_JSON,
                        help="JSON ledger output path (repository-relative)")
    parser.add_argument("--md", default=DEFAULT_MD,
                        help="Markdown ledger output path (repository-relative)")
    parser.add_argument("--top", type=int, default=DEFAULT_TOP,
                        help="roadmap length (default 50)")
    parser.add_argument("--check", action="store_true",
                        help="compare against the files on disk; exit 1 when stale")
    parser.add_argument("--quiet", action="store_true", help="only report errors")
    args = parser.parse_args(argv)

    repo_root = (Path(args.repo_root).resolve() if args.repo_root
                 else Path(__file__).resolve().parents[2])
    src_root = repo_root / DEFAULT_SRC
    functions_csv = repo_root / DEFAULT_FUNCTIONS_CSV
    csharp_root = repo_root / DEFAULT_CSHARP

    ledger = summarise(repo_root, src_root, functions_csv, csharp_root)
    ledger["nextFunctions"] = build_next_functions(ledger["coverage"]["unmapped"], args.top)
    ledger["totals"]["nextFunctionCount"] = len(ledger["nextFunctions"])

    json_text = json.dumps(ledger, indent=2, sort_keys=False, ensure_ascii=False) + "\n"
    md_text = render_markdown(ledger) + "\n"

    json_path = (repo_root / args.json).resolve()
    md_path = (repo_root / args.md).resolve()
    for out_path in (json_path, md_path):
        try:
            out_path.relative_to(repo_root)
        except ValueError:
            print("error: output path {} is outside the repository".format(out_path),
                  file=sys.stderr)
            return 2

    totals = ledger["totals"]
    if args.check:
        stale = False
        for path, expected in ((json_path, json_text), (md_path, md_text)):
            actual = read_text(path) if path.is_file() else None
            if actual != expected:
                stale = True
                print("stale: {} (rerun tools/port/generate_ledger.py)".format(
                    rel(path, repo_root)), file=sys.stderr)
        if stale:
            return 1
        if not args.quiet:
            print("port ledger is current: {} functions, {} mapped, {} unmapped".format(
                totals["inventoryFunctions"], totals["mappedFunctions"],
                totals["unmappedFunctions"]))
        return 0

    changed = []
    if write_if_changed(json_path, json_text):
        changed.append(rel(json_path, repo_root))
    if write_if_changed(md_path, md_text):
        changed.append(rel(md_path, repo_root))
    if not args.quiet:
        print("port ledger: {:,} source files, {:,} functions, {:,} game functions".format(
            totals["sourceFiles"], totals["inventoryFunctions"], totals["gameFunctions"]))
        print("coverage: {:,} mapped ({} stubs), {:,} unmapped, {:.2f}% of game code".format(
            totals["mappedFunctions"], totals["stubFunctions"],
            totals["unmappedFunctions"], totals["gameCoveragePercent"]))
        print("csharp: {:,} files, {:,} lines, {:,} address citations".format(
            totals["csharpFiles"], totals["csharpLines"],
            totals["csharpAddressCitations"]))
        print("next {} functions (first five): {}".format(
            len(ledger["nextFunctions"]),
            ", ".join(item["name"] or item["address"]
                      for item in ledger["nextFunctions"][:5])))
        if changed:
            print("wrote " + ", ".join(changed))
        else:
            print("ledger unchanged")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
