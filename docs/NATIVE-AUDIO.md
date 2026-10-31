# NATIVE-AUDIO - original HyperBlade waves in the native C# engine (N6)

This is the format and coverage reference for the original-audio path of the native C# engine.
It records the shipped wave and sound-list formats, the C# readers and player implemented in
`UBlade3.Audio`, the exact coverage measured against the retail disc, the mapping from gameplay
roles to original sound ids with citations, and the gaps that remain.

UBlade3 never copies or commits original audio. Every wave is read from the player's own
HyperBlade install at runtime; nothing in `unity/UBlade3/Assets/Scripts/UBlade3/Audio` contains
sample data.

## 1. Sources of truth

| Source | Role |
| --- | --- |
| `third_party/hyperblade-decomp` | Byte-matching decompilation of `HYPERX.EXE` v1.0.0.1. Wave parsing, the sound list, the audio manager, the mixer and every sound id used by C++ code. Citations name the source file plus the function's HYPERX address. |
| `third_party/hyperblade-20XX` | Modern C++ rebuild. `src/sound/wave.cpp` is the readable RIFF reader (the original goes through `mmio`); `tests/wave_check.cpp` is the data gate that parses every shipped wave and reads its samples back. `tools/plan_dump.py` decodes the shipped `.pbn` plans to pseudo-source, which is how the gameplay sound routing below is cited. |
| `D:\Games\HyperBlade\HYB\data` | The shipped data measured here: `Data\SNDFILES.LST` and 295 waves in `Data\Sounds`. Read-only, never part of the repository. |

The target design (N6) is: `sounds/*.WAV` -> `UBlade3.Audio` (exact reader) -> `UBlade3.Audio.AudioService`
(Unity playback). The original's AdLib music and CD audio tracks are out of scope for this
milestone (section 11).

## 2. Where the sounds live and how they are found

`src/app/gameapp.cpp` builds `g_gameApp.m_strExeDir` as `<exe folder>\Data\` and
`g_gameApp.m_strSoundsPath` as `<exe folder>\Data\Sounds`. `AudioMgr::Init` (HYPERX 0x00490990)
loads `SndFiles.lst` through `SoundManager::Load` (HYPERX 0x00449210); `AudioMgr::LoadSound`
(HYPERX 0x00490aa0) then resolves each entry's wave with `FormatResourcePath(g_gameApp.m_strSoundsPath,
pEntry->m_pszName, "wav")`.

UBlade3 locates the install through `UBlade3.Data.GameDataRoot` - the contract is unchanged from
the data and asset layers: the `HYPERBLADE_GAME_DATA` environment variable first, then
`Assets/StreamingAssets/game-data-path.txt`, then a caller-supplied default. The child folder
`sounds` is resolved case-insensitively because the disc mixes case. No original audio is
committed; the player reads the user's install.

## 3. SNDFILES.LST

`src/sound/soundmanager.cpp` `SoundManager::Load` (HYPERX 0x00449210) is the parser; the list is
also read as the plan VM's sound symbol table (`PlanVM::LoadSymbolTables`, HYPERX 0x0047f250:
each sound line becomes `id_SND_<name>`, which is how `.pbn` plans name sounds before compilation).

Rules:

* The first non-comment line is a header scanned with `sscanf("%d %s ")`; the shipped header is
  `22 kHzAnnouncer` (the leading number is unused by the loader).
* `m_nSoundCount` is (non-comment lines - 1): 363 on the shipped disc.
* Each later non-comment line scans `"%s %d %d %d"` (name, duration ms, SoundTypes group, load-mode
  flag). A failed conversion leaves the previous value in place, exactly like `fscanf`; a line with
  only a name is still an entry. A leading `#` marks a comment.
* The group column is a value of `DATA/ENUMIDS.H`'s `enum SoundTypes` for the live groups, or one of
  the sound manager's own values: `666` = `SOUND_GROUP_LOOPING` (looped) and `777` =
  `SOUND_GROUP_PRELOAD` (menu group, kept loaded outside a match), per `src/sound/soundids.h`.
  `999` is the list's "no group" marker and does not open a group.
* The loader builds a 15-entry group table (first index, count, longest duration in samples at
  44.1 per ms) as groups change contiguously; a group that reappears later overwrites its own
  first/count, which is why `sxbutt1..5`-style entries are the usable part of the shipped 777
  group. `UBlade3.Data.SoundFilesList` already ports all of these `fscanf` quirks; the audio layer
  consumes that verified list rather than reparsing it.

Shipped list facts: 363 entries; 4 names appear on two lines (`satunny6`, `decapit`, `fatality`,
`subbing`); the group column takes only six values (0 hits, 1 catches, 2 shoots, 666 loop,
777 preload, 999 none) - SoundTypes values 3-14 never occur as list groups on this disc; 42 entries
are in the preload/menu group; 29 entries carry load-mode 1 (whole-file load) and 334 carry 0;
87 entries name a wave that this install does not carry (section 6).

## 4. WAV files

### 4.1 Accepted shape

Every shipped wave is RIFF/WAVE with one `fmt ` chunk and one `data` chunk (some carry a `fact`
chunk before data). The two readers that define acceptance:

* `Wave::Open` (HYPERX 0x00493430): requires the RIFF form to be WAVE, a `fmt ` chunk of at least
  `sizeof(PCMWAVEFORMAT)` = 16 bytes, and reads the first 16 bytes as `PCMWAVEFORMAT`. If the
  format tag is `WAVE_FORMAT_PCM` (1) it sets `cbSize = 0` and never reads further; otherwise it
  reads a 2 byte `cbSize` and the extra bytes. `AudioMgr::LoadSound` then rejects a non-PCM tag
  and a non-zero `cbSize`.
* `Wave::FindDataChunk` (HYPERX 0x004935f0) seeks back to the form's data start and finds the
  `data` chunk; `Wave::ReadChunk` (HYPERX 0x00493640) reads exactly the chunk's declared bytes and
  fails if the file runs short.
* The 20XX reader (`src/sound/wave.cpp` `Wave::ReadInfo`) walks at most 64 chunks from the form
  start, accepts `fmt ` and `data` in either order, and advances by `8 + size + (size & 1)`: chunks
  are dword aligned (the odd-size pad byte).

`UBlade3.Audio.WavFile` implements exactly this: a dword-aligned chunk walk of at most 64 chunks,
the `fmt ` must be at least 16 bytes, the format tag must be PCM 1, the data chunk must fit the
file, and the block alignment and byte rate must agree with the channel and bit counts. It is
stricter than the original in one deliberate way: it throws `AudioFormatException` instead of
silently replacing unreadable audio with nothing, and it accepts only the shipped layouts (PCM,
mono or stereo, 8 or 16 bits). Anything else fails loudly.

`WavFile` exposes the parsed header, the chunk list, the data offset and byte count, the frame
count and duration, and `DecodeToFloat` converts the PCM to interleaved floats for Unity:
8-bit PCM unsigned with 128 as silence, 16-bit PCM signed little endian. `AudioClip.Create` plus
`SetData` is the equivalent of the original's whole-file load (`AudioMgr::LoadSound` with
`SOUND_LOAD_WHOLE`, load mode 1 in the list's fourth column).

### 4.2 Shipped coverage

295 wave files. All are PCM tag 1, with no `cbSize` extension. Formats:

| Rate | Bits | Channels | Files |
| --- | --- | --- | --- |
| 11025 | 8 | 1 | 6 |
| 22050 | 8 | 1 | 157 |
| 22050 | 8 | 2 | 49 |
| 22050 | 16 | 1 | 59 |
| 22050 | 16 | 2 | 23 |
| 44100 | 16 | 1 | 1 |

Totals: 35,442,651 data bytes, 19,297,803 sample frames, 875,224 ms of audio (sum of the per-file
rounded durations; the exact sum is 875,230.43 ms). Durations range from 8.5 ms to 15,173.6 ms;
the longest files are the 8-bit 22050 Hz takes, led by `sxstmo1.wav` at 15.2 s.

### 4.3 Shipped quirks, all tolerated by the original loaders

| Quirk | Files | Detail |
| --- | --- | --- |
| `fmt ` chunk size 18 | 4 | The extra word is the start of a WAVEFORMATEX `cbSize` field. `Wave::Open` reads only the 16 byte `PCMWAVEFORMAT` for a PCM tag, so the word is ignored. One file (`sxstmo1.wav`) has `20` there as unused padding; `WavFile.RawCbSize` records it and the effective `WavFile.CbSize` is 0. |
| `fact` chunk before `data` | 31 | Skipped by the chunk walk. |
| RIFF size field not equal to file length minus 8 | 40 | Every one declares one byte short (the final pad byte). The original never compares the field with the file; the reader records the mismatch. |
| Bytes after the `data` chunk | 74 | 71 are zero padding (a final pad byte or a few alignment zeros), 1 is a trailing `LIST INFO` metadata chunk (`sxarmin3.wav`) and 2 are real sample bytes beyond the declared data (`sxapprb2.wav`, `sxhhitf2.wav`). `AudioMgr::LoadSound` reads exactly the data chunk, so the trailing sample bytes are lost on this disc. Total trailing bytes: 265. |

The reader exposes all of these (chunk list, `RiffSizeMatchesFile`, `TrailingBytes`,
`TrailingData`, `TrailingChunkId`, `HasFactChunk`) so the probe can count them instead of hiding
them.

## 5. Sound ids and the audio manager

A sound id is a position in `SNDFILES.LST` counting only the sound lines, named after the wave
file (`src/sound/soundids.h`). The C++ code names a few of them; everything else, especially the
plan-driven sounds, is named at plan level as `id_SND_<list name>` and compiled to the same index
(`PlanVM::LoadSymbolTables`).

Playback paths in the decompilation:

* `AudioMgr::PlaySpecific(int nVoice)` (HYPERX 0x00490cc0): plays one entry, reloading it if it
  was not preloaded; a playing non-looping sound may restart.
* `AudioMgr::PlayGroup(int nGroup)` (HYPERX 0x00490c80): `SoundManager::PickFromGroup`
  (HYPERX 0x00449680) returns the group's first or second entry, alternating on a shared counter
  (`g_wSoundSequence`), and then plays it. `AnimEvent::PlaySndType` (HYPERX 0x00406ef0) is the
  plan action that calls this with an `enum SoundTypes` value.
* `DSoundMgr::Play` (HYPERX 0x00490780) takes one of eight DirectSound channels
  (`DSOUND_CHANNEL_COUNT` 8) and starts a group-666 entry with the looping flag.
* `AudioMgr::PreloadGroup(SOUND_GROUP_PRELOAD)` loads the menu group outside a match
  (`src/engine/game.cpp` line 305); `FreeGroupBuffers` releases it (line 542).

## 6. The shipped install's gaps

The retail data folder used for this port carries 295 waves for 363 list entries. The missing
entries are concentrated exactly in the original front end:

* 87 entries have no `Data\Sounds\<name>.wav`. They include most of the preload/menu group
  (777): `button` 1-4, `bigdoor`, `door`, `screenon`, `fatality`, `health`, `immunity`, `injury`,
  `longshot`, `multiplier`, `period`, `slowdown`, `subbing`, `team`, the fourteen team-name
  sounds, the theme tunes (`chicthem`, `houthm2` ... `warthm2`), plus many alternate takes
  (`Sxrctha4..7`, `Sxvhitb3/g3/j3`, several `Sxlndg*`/`Sxxbrke*`/`Sxrglid*`) and a few voices.
  `AudioMgr::LoadSound` increments the entry's load state on a failed open and leaves it silent;
  the game runs with those sounds unavailable.
* 23 waves are named by no list entry (`sabeard2.wav`, `sadude2.wav`, `salook4.wav`,
  `salook5.wav`, `saloser2.wav`, `sangm3.wav`, `sapigdog.wav`, `sarass2.wav`, `sawchj2a.wav`,
  `sfelbow.wav`, `sxapprc2.wav`, `sxbutt6.wav`, `sxcnfrn3.wav`, `sxdoor1.wav`, `sxdoor2.wav`,
  `sxdoor3.wav`, `sxhghpw3.wav`, `sxhghst3.wav`, `sxscrch.wav`, `sxthrwc6.wav`,
  `sxthrwc7.wav`, `sxximnty.wav`, `sxxppos2.wav`). The original sound manager never opens them.
* 4 list names are duplicated; both lines resolve to the same file.

The C# reader and player treat all of this as shipped-install data, not parser failure: the probe
counts it, AudioService logs it as warnings, and a role whose file is missing simply answers false
when played. Every role `UBlade3.Audio` plays by default does have its file on this install.

## 7. Gameplay roles and their original sounds

The `UBlade3.Audio.AudioRole` type names the sounds the native engine plays, with the list index,
the wave stem and the play route. Indices are positions in `SNDFILES.LST`'s sound lines; the
group is the list's third column. All of these files are present on the measured install.

| Role id | Index | Wave | Group | Original route |
| --- | --- | --- | --- | --- |
| `menu-ui` | 289 | `sxbutt1.wav` | 777 (preload/menu) | `src/ui/uiscreen.cpp` lines 451-568 play `PlaySpecific(SOUND_SXBUTT1)` from the screen button handlers; `soundids.h` defines `SOUND_SXBUTT1 0x121`. The list line is `sxbutt1 0 777 #292-3`. |
| `impact-first` | 0 | `sxhhite2.wav` | 0 = `id_STP_Hit` | `Data/plans/listen.pbn` `SET_MESSAGE_INTERACTOR id_MSG_Hit` executes `DO_ACTION id_ACF_PlaySndType(0)`. `AnimEvent::PlaySndType` (0x00406ef0) calls `PlayGroup(0)`; `SoundManager::PickFromGroup` alternates the group's first two entries, indices 0 and 1. `soundids.h` defines `SOUND_SXHHITE2 0`. |
| `impact-second` | 1 | `sxhhitf2.wav` | 0 = `id_STP_Hit` | The other half of the same group-0 alternation. |
| `goal-1x` | 74 | `sxrscra2.wav` | 999 (no group) | `Data/plans/ngoal.pbn` and `sgoal.pbn`: `SET_MESSAGE_INTERACTOR id_MSG_NGoalScored / id_MSG_SGoalScored`, `DECIDE_BY_AMONG id_DCF_RokScoreValue()` arm 0, `DO_ACTION id_ACF_PlaySound(74)`. |
| `goal-2x` | 75 | `sxrscra3.wav` | 999 | The same handlers' 2-point arm (`PlaySound(75)`). |
| `goal-4x` | 76 | `sxrscra4.wav` | 999 | The same handlers' 4-point arms (`PlaySound(76)`). |
| `crowd-goal-cheer` | 18 | `sxcrwdc2.wav` | 999 | `sgoal.pbn` sends `id_MSG_PlayCrowdResponse` with data 0/1/2 to slot 24; `listen.pbn`'s `id_MSG_PlayCrowdResponse` handler waits 53 ms and on `id_MSG_GotIt` plays `PlaySound(18/19/20)`, i.e. `sxcrwdc2`/`sxcrwdd2`/`sxcrwde2`. |
| `crowd-goal-boo` | 21 | `sxcbooa2.wav` | 999 | `ngoal.pbn` sends `id_MSG_PlayCrowdResponse` with data 3/4/5; `listen.pbn` maps those to `PlaySound(21/22/23)`, i.e. `sxcbooa2`/`sxcboob2`/`sxcbooc2`. |
| `crowd-ambient` | 15 | `sxxambb2.wav` | 666 = `SOUND_GROUP_LOOPING` | `src/engine/game.cpp` lines 312 and 587 play `SOUND_SXXAMBA2`/`SOUND_SXXAMBB2`; `DSoundMgr::Play` starts a group-666 entry looping. On this install `Sxxamba2` (entry 14) is absent, so `Sxxambb2` (entry 15) is the usable loop. |

Notes:

* The two goal sounds are complements: the goal object's plan also sends the crowd-response
  message, so a goal produces the level-appropriate `sxrscra*` sound plus a crowd cheer (south
  goal) or boo (north goal) after the 53 ms gap the plan sets with `SET_TIMEOUTMSG 53`.
* `listen.pbn` also routes collisions (`id_MSG_OpponentCollision`) to large banks of impact/voice
  takes; those are exposed through `AudioService.PlayIndex/PlayName` for the N6 work but are not
  yet driven by the match.
* The decompilation's own `soundids.h` names a few more ids used from C++: `screenon` (entry 256,
  absent on this install), `dooropn1`/`doorcls1` (entries 294/295, present), `endsir` (331,
  present), `injure1` (188, present), the cheat sounds `sxstmo1` (190, present) and `sasrles2`
  (69, present). They are available by index/name as well.

## 8. C# API

All types are in namespace `UBlade3.Audio` under
`unity/UBlade3/Assets/Scripts/UBlade3/Audio`. Dependency split:

* `WavFile.cs`, `AudioFormatException.cs` and `AudioRole.cs` use only the BCL.
* `SoundCatalog.cs` and `AudioProbe.cs` use `UBlade3.Data` (`GameDataRoot` and the verified
  `SoundFilesList`) to locate and parse the player's install; they use no UnityEngine type.
* `AudioService.cs` and `AudioBootstrap.cs` add UnityEngine for playback and the optional hook.

| File | Surface |
| --- | --- |
| `WavFile.cs` | `WavFile.Load/LoadFile`, `DecodeToFloat`, `WavChunk`; header fields, `FrameCount`, `DurationSeconds/DurationMilliseconds`, `RiffSizeMatchesFile`, `TrailingData/TrailingBytes`, `HasFactChunk`, `RawData` |
| `AudioFormatException.cs` | `AudioFormatException` with source name and byte offset |
| `AudioRole.cs` | `AudioRole` (`Id`, `Index`, `SoundListName`, `Group`, `Purpose`, `PlayRoute`, `Citation`) and the nine required roles |
| `SoundCatalog.cs` | `SoundCatalog.Load`: the list, the case-insensitive file map, missing entries, orphan files, duplicate names, `TryLoadWave` |
| `AudioProbe.cs` | `Describe/Check/Run/Report/Reset/Digest/LastError` and the expected-count constants |
| `AudioService.cs` | Unity `MonoBehaviour`: `PlayRole/PlayMenuUi/PlayImpact/PlayGoal/PlayGoalCrowd/PlayCrowdAmbient/PlayIndex/PlayName`, eight-channel pool, lazy `AudioClip` cache, `MasterVolume` |
| `AudioBootstrap.cs` | default-off bounded hook: `InstallAudio()` plus the `--ublade3-audio` / `UBLADE3_AUDIO=1` auto-install |

Typical use:

```csharp
SoundCatalog catalog = SoundCatalog.Load();
WavFile wave = WavFile.LoadFile(catalog.FindFile("Sxhhite2"));
float[] samples = wave.DecodeToFloat();          // interleaved, mono or stereo, -1..1

// In a scene:
AudioService audio = AudioBootstrap.InstallAudio();   // or add the component
audio.PlayMenuUi();
audio.PlayImpact();                                   // alternates group 0, indices 0/1
audio.PlayGoal(4);                                    // 1 -> 74, 2 -> 75, 4 -> 76
audio.PlayGoalCrowd(true);                            // cheer (18) / boo (21)
audio.PlayCrowdAmbient();                             // looping group 666, index 15
```

A malformed or unsupported wave is an `AudioFormatException` naming the file and offset; the
service never throws into a game frame (it logs `UBLADE3_AUDIO_SOUND_MISSING` and answers false),
and the probe is the loud gate that fails the build.

## 9. AudioProbe output contract and verified run

`AudioProbe` is scene free. `Check()` prints the report then throws on a failed load or an
expected-count/digest mismatch; `Run()` is Check() as a process exit code; `Describe()` prints one
line; `Report()` returns the text. It parses every `.wav` under `Data\Sounds`, joins all 363
`SNDFILES.LST` entries, checks the nine roles and computes digests.

```
UBLADE3_AUDIO_OK files=295 entries=363 durationMs=875224 digest=1a8d0a92b89a6019
UBLADE3_AUDIO_DETAIL data=<Data path> sounds=<sounds path>
UBLADE3_AUDIO_COUNTS files=295 entries=363 resolvedEntries=276 missingEntries=87 orphanFiles=23 duplicateNames=4 dataBytes=35442651 frames=19297803 durationMs=875224 factChunkFiles=31 fmtChunk18Files=4 riffSizeMismatchFiles=40 trailingByteFiles=74 trailingZeroFiles=71 trailingMetadataFiles=1 trailingSampleBytesFiles=2 trailingBytes=265 roles=9 roleFiles=9
UBLADE3_AUDIO_FORMATS 11025/8/1=6 22050/8/1=157 22050/8/2=49 22050/16/1=59 22050/16/2=23 44100/16/1=1
UBLADE3_AUDIO_ROLES menu-ui=289:Sxbutt1 impact-first=0:Sxhhite2 impact-second=1:Sxhhitf2 goal-1x=74:Sxrscra2 goal-2x=75:Sxrscra3 goal-4x=76:Sxrscra4 crowd-goal-cheer=18:Sxcrwdc2 crowd-goal-boo=21:Sxcbooa2 crowd-ambient=15:Sxxambb2
UBLADE3_AUDIO_DIGEST files=6dc8e8278c0ebaf3 lists=915aa6d967e98d23 roles=2c679fa1237757dd total=1a8d0a92b89a6019
UBLADE3_AUDIO_WARNING <shipped-install gaps: 87 missing entries, 23 orphan files, 4 duplicate names, 2 sample-byte tails>
UBLADE3_AUDIO_WARNINGS 4
UBLADE3_AUDIO_CHECK mismatches=0
```

The four warnings are the shipped-install gaps from section 6; none of them makes the probe fail,
because the original loader tolerates all of them (missing entries stay silent, orphans are never
opened, duplicate lines collapse to one file, trailing sample bytes beyond the declared data chunk
were never read by the original). Everything else - an unsupported tag, depth or channel count, a
truncated chunk, a missing `fmt ` or `data ` chunk - is fatal.

The digest is FNV-1a 64 over a canonical stream: every parsed header field and chunk id/size, each
file's PCM data bytes, every `SNDFILES.LST` entry (including the inherited-value flags), the group
table and the role table; the total hashes the three category digests. `ExpectedTotalDigest` is
pinned to `1a8d0a92b89a6019`, so a reader or data change fails the gate.

## 10. Verification performed

* C# 9 compile (`LangVersion 9.0`, `dotnet build`): 0 warnings, 0 errors for the dependency-free
  part against `net9.0`, against `netstandard2.1` (the Unity profile), and - together with
  `UBlade3.Data` - the Unity-facing files against the real Unity 6000.6.7f1 managed assemblies
  (`UnityEngine.CoreModule.dll`, `UnityEngine.AudioModule.dll`, `UnityEngine.ScriptingModule.dll`).
* A full Unity 6000.6.7f1 batch-mode import/compile of the project was run after checking that no
  other Unity process held it (Unity is shared with the other native-engine agents):
  `Unity.exe -batchmode -quit -nographics -projectPath unity/UBlade3 -logFile <temp log>` exited 0
  with zero `error CS`/compilation-failure lines, the rebuilt
  `Library/ScriptAssemblies/Assembly-CSharp.dll` contains the `UBlade3.Audio` types and the
  `UBLADE3_AUDIO_OK` / `UBLADE3_AUDIO_SERVICE_READY` literals, and Unity generated the `.meta`
  files beside the new sources (untracked; the Lead commits them). The same batch run also
  imported the other agents' concurrent `UBlade3.PlanVM` work, so it is an in-engine compile of
  the tree as it stood, not only of this namespace.
* `AudioProbe` against `D:\Games\HyperBlade\HYB`: the section 9 output, `mismatches=0`, digest
  stable across two fresh reports.
* A 23-case self test passed: every shipped wave parses, the 295 files / 363 entries / 87 missing /
  23 orphan / 4 duplicate counts hold, all nine role waves load, truncated and wrong-tag/wrong-depth
  /wrong-channel synthetic waves throw, and two reports are byte identical.
* Independent cross-check against a separate Node implementation of the same PCM conversion: for
  one 8-bit mono 11025 Hz file, one 8-bit mono 22050 Hz file, one 16-bit mono 22050 Hz file, one
  16-bit stereo 22050 Hz file, one 8-bit file with a `fmt ` 18 chunk, one `fact`-chunk file, one
  44100 Hz `LIST`-tail file and one 8-bit theme file, the decoded float hash, first samples and
  sum match the C# `WavFile.DecodeToFloat` byte for byte.
* No original audio is committed; the repository holds only source and documentation.

## 11. Gaps and deliberately not done

1. **Not wired into the native match by default.** The bounded hook (`AudioBootstrap`) installs an
   `AudioService` only with `--ublade3-audio` or `UBLADE3_AUDIO=1`; nothing in `UBlade3.Native`,
   the scenes, the editor build or the release was edited. The next N6 step is to call
   `PlayImpact`/`PlayGoal`/`PlayGoalCrowd` from the match events once the N5 event path exists.
2. **Original AdLib music and CD audio** are out of this milestone. `AudioMgr::PlayCDTrack` (MCI
   CD audio) and the AdLib/mixer music paths are not implemented; no music is played.
3. **87 referenced waves are absent from this install**, mostly menu/theme sounds; the original
   would play silence for them too. The complete menu group needs a fuller install.
4. **DirectSound mixer volume/pan parity** is not reproduced: `MasterVolume` is a Unity linear
   volume, whereas the original fades the Windows mixer line and sets per-buffer volume/pan in
   hundredths of a decibel. Playback is 2D (as the original buffers are).
5. **Eight channels.** The original has eight DirectSound channels (`DSOUND_CHANNEL_COUNT`) shared
   by all groups; AudioService mirrors that with eight `AudioSource`s and restarts the oldest
   implicitly via `PlayOneShot`. The 15 SoundManager slots are list bookkeeping, not channels.
6. **Streaming load modes.** The list's fourth column marks 29 entries for whole-file load in the
   shipped data; others were streamed from disk in the original. AudioService decodes every clip
   whole into RAM (all 295 waves total about 35 MB of PCM), which is bounded and simple; a
   memory-constrained build could stream, but the shipped set is small.
7. **The 12 trailing sample bytes** in `sxapprb2.wav` and `sxhhitf2.wav` are dropped exactly as
   the original drops them; they are reported by the probe rather than played.
8. **Spatial audio** (3D attenuation) is not in the original and is not claimed; the Unity sources
   are 2D.

## 12. Legal and provenance

The original waves are read from the user's own HyperBlade install and are never copied into this
repository. `hyperblade-decomp` and `hyperblade-20XX` are read-only references for format research;
the C# code and documentation here are written from that research. This namespace is new UBlade3
code: it reuses only the already-verified `UBlade3.Data` readers and uses no UBlade2 asset or
UBlade2 source file.
