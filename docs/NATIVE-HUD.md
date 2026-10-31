# NATIVE-HUD - original InfoBar layout and GDI MS Sans Serif text (N12)

This is the bounded N12 HUD slice: the original InfoBar layout from src/ui/infobar.cpp
(LayoutSurfaces 0x00450e60 and the surface constructor 0x00451280), the 20XX HudLayer integer
scaling, original Data\assets8 HUD art, the real installed Windows GDI "MS Sans Serif" face through
the exact CreateFontA/GetTextExtentPoint32A calls, damage bars, radar circle/blips, shot-power
head-camera frame and the period/score/clock/team/roster positions. It is a HUD/layout slice, not a
parity or release claim. Nothing was committed; the Lead owns the commit.

Repository: C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3.

## 1. Sources of truth

| Source | Use |
| --- | --- |
| src/ui/infobar.cpp LayoutSurfaces HYPERX 0x00450e60 (lines 744-939) | Exact period/clock, speed frame, team frames/logos, damage bar positions, scores, ROK value, message frame, radar, network icon, head-camera frame, shot power, reticle and roster positions. |
| src/ui/infobar.cpp InfoBar::InfoBar 0x00451280 (lines 963-1105) | Art names (frper, frdamgA/B, frspeed, frradar, frshot, frmsg, network, nhshotp, Reticl2), font heights 9/10/11/15/20, damage-bar strip parameters (12 frames, unit 9, heights 2..8), palettes. |
| src/ui/infobar.cpp DrawScene and DrawPeriodLight | Draw order, period lamp offsets 19/38/57 at +0x28, 9x9 lamp, speed/damage/radar/head-cam passes. |
| src/ui/infobar.h, infobarteams.h, graphics/infobarslot.cpp 0x0044f6d0 | Damage-bar segment geometry and team colours: followed 0xffff, reserve 0xff00, on-drome 0x100ff, absent 0. |
| src/graphics/infobarradar.cpp 0x0044fcd0/0x0044fe70/0x004500e0 | Radar scale/centre from the frame ((w-4)/2, (h-4)/2, max radius), blips 4x4, 170-unit cull, trail, the arrow triangle for a carried ROK, colours. |
| src/graphics/cddtext.cpp 0x0044fa40/0x0044faf0/0x0044fc10 | CreateFontA(height, 0,0,0, FW_MEDIUM, 0,0,0,0,0,0, NONANTIALIASED_QUALITY, VARIABLE_PITCH, "MS Sans Serif"), GetTextExtentPoint32A measures, TextOutA draws. |
| third_party/hyperblade-20XX/src/graphics/hudlayer.cpp | HUD layer scaling: kHUDLines=480, scale=max(1, backBufferHeight/480), layer width aligned to 4, origin centred. |
| D:\Games\HyperBlade\HYB\data\assets8 | The shipped HUD art read at runtime; no art is copied into the repository. |

## 2. What changed

New files:

| File | Contents |
| --- | --- |
| Native/MatchHudFont.cs | Runtime GDI MS Sans Serif rasteriser: exact CreateFontA parameters/heights, GetTextExtentPoint32A metrics, TextOutA glyph coverage into the HUD layer. No font file is committed; the installed Windows face is used at runtime and the actual face name is checked. |
| Native/MatchHudOriginal.cs | Original InfoBar renderer: loads the extra assets8 art, computes the decomp layout, draws the HUD layer, damage bars, radar blips/arrow/trail, period lamps, shot-power/head-camera frame/reticle, message/status, team names/roster labels and composites into the capture framebuffer or OnGUI. |
| Native/MatchHudProbe.cs | Layout/art/text probe: compares the ported rectangles against the decomp formulas at 640x480 and the 720p/1080p/1440p/4K layer scales; checks the documented art dimensions and GDI font availability/metrics. |
| docs/NATIVE-HUD.md | This document. |

Edited:

* Native/MatchHud.cs: default-off OriginalLayout/OriginalHud hook, the original probe-state fields,
  and delegation in OnGUI/Composite. The legacy layout path is untouched when the flag is false.
* Assets/Editor/UBlade3MatchPlay.cs: CheckHud and CaptureHud entry points.

Regression safety: the original layout is opt-in. MatchHudOriginal.ShouldUseByDefault() only turns it
on for an interactive player (Application.isPlaying && !Application.isBatchMode) with no
capture/check argument (--play-capture, --n7-capture, --display-probe, --hud-capture). Therefore all
N4-N11 Editor/player capture paths keep the legacy layout and their byte-identical artifacts (N5's
seven captured PNGs are re-hashed below). The N12 probe/capture enables the original layout
explicitly.

## 3. Original layout at 640x480

The probe prints and verifies these rectangles after loading the real art (frper 83x60, frdamgA/B
220x47, logos 73x21, frspeed 162x51, frradar 139x136, frshot 169x130, frmsg 246x25, network 32x32,
nhshotp 102x12, reticl2 31x31):

| Item | 640x480 layer rect/point | Citation |
| --- | --- | --- |
| period frame | 278,2 83x60 | (W-83)/2, y=2 |
| clock text | 304,9 | period centre, y=9; font height 20 |
| speed frame | 0,0 162x51 | infobar.cpp:765-768 |
| roster start / frame rate | 6,51 / 20,71 | speed frame size + 0 / +0x14 |
| team A frame | 0,383 220x47 | H-2*47-3 |
| team A logo | 2,405 | frame + 2,0x16 |
| team A score / ROK | 188,393 / 185,385 | frameW-0x20, frame+0xa / -3,-8 |
| damage A rows | 78,407 and 78,419 | 0x4e, frame+0x18 and +0xc |
| team B frame | 0,428 220x47 | H-47-5 |
| team B logo / score | 2,432 / 188,453 | frame+4 / frame+0x19 |
| damage B rows | 78,432 and 78,444 | 0x4e, frame+4 and +0xc |
| message frame/text | 200,416 246x25 / 206,421 | teamW-0x14, teamBframe-0xc; +6,+5 |
| radar frame | 481,324 139x136 | W-radarW-0x14, H-radarH-0x14 |
| network icon | 588,20 32x32 | W-networkW-0x14, 0x14 |
| head-camera frame | 471,0 169x130 | W-shotW, y=0 |
| head-camera text | 501,130 | frame + 0x1e, shotH |
| shot power art | 506,16 102x12 | frame + 0x23,0x10 |
| reticle | 496,25 31x31 | frame + 0x19,0x19 |
| period lamp | period + 19/38/57, y+0x28, 9x9 | g_anPeriodLightOffset, PERIOD_LAMP_TOP/SIDE |

The HUD layer scales exactly as 20XX HudLayer:

| Screen | Layer | Scale | Origin |
| --- | --- | --- | --- |
| 1280x720 | 1280x720 | 1 | 0,0 |
| 1920x1080 | 960x540 | 2 | 0,0 |
| 2560x1440 | 852x480 | 3 | 2,0 |
| 3840x2160 | 960x540 | 4 | 0,0 |
| 640x480 design | 640x480 | 1 | 0,0 |

At 1080p and 4K the layer is 960x540 and is scaled 2x/4x, so the HUD keeps about 540 lines of
layout and stays readable; at 720p it is native 1x on a 1280x720 layer, the original integer-layer
behaviour.

## 4. Original art and text

All art is read from the player's own Data\assets8 at runtime through the existing BmpReader
(MDM1-wrapped raw DIBs). The decoded dimensions are: frper 83x60, frdamgA/B 220x47, hudChi/hudsea
73x21, frspeed 162x51, frradar 139x136, frshot 169x130, frmsg 246x25, network 32x32, nhshotp 102x12,
reticl2 31x31. The corner pixel is the colour key, exactly as the earlier HUD art path.

Text is the real GDI face, not the interim 3x5 bitmap font:

    CreateFontA(height, 0, 0, 0, FW_MEDIUM, 0, 0, 0, 0, 0, 0,
                NONANTIALIASED_QUALITY, VARIABLE_PITCH, "MS Sans Serif")

with heights 9 (roster/ROK), 10 (head-cam label), 11 (team names), 15 (scores/message) and 20
(clock). GetTextExtentPoint32A supplies the exact layout width/height; TextOutA supplies the glyph
pixels. No font file is committed: the game uses the installed Windows face at runtime and checks
GetTextFaceA; if the face is unavailable it logs UBLADE3_NATIVE_HUD_FONT ... available=False and
draws no text rather than substituting an unrelated face.

Observed GDI metrics on the verification host (Windows 11, 6000.6.7f1):

    requested="MS Sans Serif" actual="MS Sans Serif" available=True sizes=9,10,11,15,20
    h9="0:00"=>21x13 h10="0:00"=>21x13 h11="0:00"=>21x13 h15="0:00"=>21x13 h20="0:00"=>31x20

The host's GDI font mapper collapses requested cell heights 9/10/11/15 to the same 13 px cell and
keeps 20 as 20 px. This is a host/GDI measurement fact: the port issues the same CreateFontA call as
the original, so the hit is recorded as a text-metric gap rather than hidden (section 6).

## 5. HUD probe and captures

### 5.1 Headless probe

    Unity.exe -batchmode -quit -nographics -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.CheckHud -logFile <temp>/n12-hud-check.log

Exit 0, UBLADE3_NATIVE_HUD_CHECK_OK; UBLADE3_NATIVE_HUD_PROBE_COUNTS mismatches=0 missing=0
fontAvailable=True art=523. The probe logs every art dimension, each of the layout rectangles above
against its independent expected value, the four HUD-layer scales, and the GDI metrics.

### 5.2 Editor captures (graphics)

    Unity.exe -batchmode -quit -projectPath unity/UBlade3 ^
        -executeMethod UBlade3.EditorTools.UBlade3MatchPlay.CaptureHud -logFile <temp>/n12-hud-capture.log

Exit 0, UBLADE3_NATIVE_HUD_CAPTURES_OK frames=4. The capture state is pinned for a deterministic HUD
probe: shot power 72%, head-camera frame/reticle visible at (0.62,0.5), speed ratio 0.65, status
"MATCH LIVE", sim tick 60. This exercises the shot-power/head-camera/reticle widgets in the frames;
it is labelled UBLADE3_NATIVE_HUD_CAPTURE_PROBE_STATE and is not a claim that a shot was taken.

| Frame | File | Size | Bytes | SHA-256 prefix |
| --- | --- | --- | --- | --- |
| 720p | artifacts/native-hud-720p.png | 1280x720 | 132379 | 2efa8c9a60dc2ab7 |
| 1080p | artifacts/native-hud-1080p.png | 1920x1080 | 249741 | c94bf0b53fd8d5c0 |
| 4K | artifacts/native-hud-4k.png | 3840x2160 | 722345 | 50fa1f87b28a706c |
| design | artifacts/native-hud-design-640x480.png | 640x480 | 60390 | a3e5a68e66210796 |

The frames show the original layout components: speed frame top-left, team name/logo/damage bars,
period frame/clock/lamps, shot camera/shot power/reticle at top-right, roster rows and message frame,
and the radar circle/blips/arrow bottom-right. The 640x480 design frame is the exact decomp design
space; 720/1080/4K are the same layer at HUD scale 1/2/4.

## 6. Verification notes and gaps

* The 9/10/11/15 GDI heights collapsing to a 13 px cell on this host is recorded above; the face
  itself is exact, the size mapping is a host GDI property. A future option is to derive exact pixel
  heights from GetTextMetrics, but that would change the documented original call.
* Roster names/formation seats are not in the simulation: rows 1-2 show n. TEAM F1/F2 plus the
  damage value from Stats.GetScalar(RATING_DAMAGE); reserve rows 3-4 are blank. The original's
  four-row roster needs the plan/formation/bench systems, which are not ported.
* The speed frame art is drawn but the dynamic speed-meter strip (speed01) is not; frame-rate text,
  the network icon and the full head-camera video inset are not drawn. The head-camera frame, shot
  power bar and reticle are drawn from pinned probe state in the captures; the interactive player
  reads the live MatchPlay control for those values.
* Radar geometry, blips, trail, arrow and colours are ported; the original frame 7/8 marker art is
  represented by the filled triangle (SWRend::FillPolygon behaviour), not by a bitmap frame.
* Text uses the installed Windows face; none is shipped. If the face is missing, no text is drawn
  and the probe fails rather than falling back to an unrelated font.
* The legacy HUD remains the path for every existing deterministic capture (N4-N11); the original
  layout is the interactive default. A player capture of the default-on interactive HUD was not
  added in this slice; the N12 captures enable the original renderer explicitly and the headless
  probe verifies its layout.

## 7. Regression evidence

The N12 hooks are default-off and the N5/N6/N7/N11 capture paths were not changed. After the N12
changes (all exit 0):

    UBLADE3_NATIVE_MATCH_CHECK ticks=150 seconds=5 mismatches=0 digest=f54169057f5800dd
    UBLADE3_NATIVE_GAMEPLAY_CHECK_COUNTS ... homeScore=2 awayScore=1 goals=2 mismatches=0
    UBLADE3_NATIVE_GAMEPLAY_CHECK_OK
    UBLADE3_NATIVE_PLAY_CHECK_OK (pickups=2 shots=1 passes=1 goals=1 score=1-0)
    UBLADE3_NATIVE_MATCH_N7_CHECK_OK
    UBLADE3_DISPLAY_CHECK_OK

The seven N5 Editor captures were regenerated and byte-compared to the committed blobs with
git hash-object / git rev-parse HEAD:...: all identical. No Unity or 20XX process was left running by
these runs. python scripts/check_all.py PASS (187 text files in the shared working tree at verification time;
other teammates' concurrent N10/N11/N12 files are also present).

## 8. File list

New:

* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchHudFont.cs (+ .meta)
* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchHudOriginal.cs (+ .meta)
* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchHudProbe.cs (+ .meta)
* docs/NATIVE-HUD.md (this file)
* artifacts/native-hud-720p.png, native-hud-1080p.png, native-hud-4k.png,
  native-hud-design-640x480.png

Edited:

* unity/UBlade3/Assets/Scripts/UBlade3/Native/MatchHud.cs: default-off original-layout hook and
  probe-state fields; delegation in OnGUI/Composite.
* unity/UBlade3/Assets/Editor/UBlade3MatchPlay.cs: CheckHud and CaptureHud.
