# DATA-FORMATS - HyperBlade 1996 original data formats (UBlade3 U3-M1)

This is the exact format reference established by reading the byte-matching decompilation
(`third_party/hyperblade-decomp`, UBlade3's source of truth) and the original retail data
(`D:\Games\HyperBlade\HYB\data`, not committed), then implemented by the dependency-free C# layer in
`unity/UBlade3/Assets/Scripts/UBlade3/Data`. It records field order, units, loader quirks and the
fields that remain unresolved.

The market data set is 14 teams (`*.HTM`), 14 dromes (`*.ENV`) and the config/catalogue files listed
below. UBlade3 never copies this data into the repository; it is read from the player's install.

## 1. Where the data comes from

The original executable resolves every name against its own directory (`g_gameApp.m_strExeDir`) and
keeps the data in `Data\`. UBlade3 resolves case-insensitively (the disc mixes case: TEAMS.CAT names
"Chico.htm" while the file is "chico.htm"; the loader string is "teams.cat" while the file is
"TEAMS.CAT"). Priority order, implemented in `GameDataRoot`:

1. `HYPERBLADE_GAME_DATA` environment variable - the install root containing `Data\` (for example
   `D:\Games\HyperBlade\HYB`). A path to the `Data` folder itself is also accepted.
2. `Assets/StreamingAssets/game-data-path.txt` - one path per line, `#` comments allowed. The
   streaming-assets file is found through the explicit path passed by the caller, then through the
   usual Unity layout discovered from the current directory / player base directory.
3. A caller-supplied default install path (`defaultInstallPath` parameter).

If no folder containing `Data\` is found the loader throws a clear `DataNotFoundException` listing
every location it tried (the port contract requires failing loudly instead of falling back to mock
data).

## 2. Common text rules (all `*.HTM`, `*.ENV`, `*.CFG`, `*.CAT`, `*.LST`, `*.INI`, `*.TXT`)

- Shipped text files are pure ASCII with CRLF line ends (checked byte-for-byte; the only binary files
  in `data` are `palette.gif` and `custom.pal`). The reader decodes each byte to the same code point
  (Latin-1 style) so no byte is silently replaced.
- `FileObj::GetNextLine` (`src/common/fileobj.cpp` lines 169-220, HYPERX 0x004406b0) treats CR, LF
  and CRLF as line ends, folds a run of consecutive line breaks into the line before, and returns a
  zero length only at the end of the file. A zero length ends the caller's
  `while (GetNextLine(&line) > 0)` walk. Lines are capped at 0x100 characters; the shipped HTM/ENV
  lines are at most 155 characters, so the cap is never hit.
- Token rules (`src/common/parseutil.cpp`, `ParseUtil`):
  - `ParseToken` (0x00487370) skips leading spaces/tabs, then takes characters until a delimiter from
    the caller's set. All loaders here pass a null first delimiter set, so a token starts at the first
    non-blank character. A token running to the end of the line is returned whatever its length (the
    original uses `strlen` there).
  - `FindKeyword` (0x00435f90) is a case-insensitive `_mbsicmp` over a keyword table; first match
    wins, -1 means unknown.
  - `SkipToChar`/SkipPastChar` (0x00487420/0x00487460) skip spaces/tabs and then require the stop
    character (`=` in every loader here).
- Numeric fields are converted with `sscanf` formats `%f`, `%d`, `%hd` applied to a token, so trailing
  characters are ignored and a failed conversion is fatal (or, in the DFT readers, leaves the variable
  unchanged - see section 9).

## 3. TEAMS.CAT

One team file name per line, in catalogue order. `#` starts a comment line.
`TeamsBlock::TeamsBlock` (`src/engine/teamsblock.cpp` lines 36-126, HYPERX 0x004847d0): at most 30
teams (the 31st is refused), a file name of 32 characters or more is fatal, and an empty catalogue is
fatal. The catalogue spelling is saved into `TeamBlock::szPath`.

Shipped order (index 0-13) and the `DefineTeam` header each file carries:

| # | TEAMS.CAT | name | environment | dromext | idlogo | tsanim |
|---|---|---|---|---|---|---|
| 0 | Chico.htm | The Machine | chidro.env | chiext | hudChi | chipl |
| 1 | Houst.htm | The LoneStars | houdro.env | houext | hudHou | houpl |
| 2 | Jamca.htm | The DredNoks | jamdro.env | jamext | hudJam | jampl |
| 3 | Vegas.htm | The BlackJack | vegdro.env | vegext | hudVeg | Vegpl |
| 4 | Leeds.htm | The Knights | lnddro.env | lndext | hudLnd | lndpl |
| 5 | Losan.htm | The Shockwave | losdro.env | losext | hudLos | Lospl |
| 6 | Mscow.htm | The T-34s | mosdro.env | mosext | hudMos | mospl |
| 7 | Munic.htm | The Sturmnacht | mundro.env | munext | hudMun | munpl |
| 8 | Nem.htm | BLAM! Nemesis | Nemdro.env | Nemext | hudNEM | Nempl |
| 9 | Nyork.htm | The Glory | newdro.env | newext | hudNew | newpl |
| 10 | Seattle.htm | The Fury | seadro.env | seaext | hudsea | seapl |
| 11 | Tokyo.htm | The Ronins | tokdro.env | tokext | hudTok | tokpl |
| 12 | Toron.htm | The Crusade | tordro.env | toreext | hudTor | torpl |
| 13 | Warsw.htm | The Ambush | wardro.env | warext | hudWAR | warpl |

(The actual file names on the disc are lower case, e.g. `nemdro.env`; resolution is case-insensitive.)

## 4. *.HTM - team files

Reference: `ParseTeamHTMFile` (HYPERX 0x00482e70, `src/engine/gameengine.cpp` lines 1945-2161),
`LoadTeamFromFile` (HYPERX 0x004858b0, lines 3210-3403), `TeamBlock`/TeamPlayerRow
(`src/engine/teamblock.h`, `src/engine/teamplayerrow.h`), ratings (`src/engine/statids.h`,
`DATA/ENUMIDS.H` lines 256-315).

### 4.1 Header: one `DefineTeam` line

`DefineTeam name=... idlogo=... tslogo=... tstext=... tstitle=... tsanim=... environment=... dromext=...`

The parser first scans forward for a line whose first token is `DefineTeam`, then reads keyword/value
pairs from the rest of that line. Keyword table (order used by `FindKeyword`):
`name, idlogo, tslogo, tstext, tstitle, tsanim, environment, DefineTeam, dromext`.
- `_` in `name` becomes a space ("The_Machine" -> "The Machine").
- Keyword 7 (`DefineTeam`) has no handler and stops the header walk, as does the first unknown token
  or a keyword without `=`.
- A missing `name` is fatal ("Incomplete Team def"). The other fields may stay empty.
- Decomp note: the environment and dromext error messages name each other's field (the original's
  copy-paste bug); the field order is fixed by the table above.
- `environment` names the `.ENV` file; `dromext` names the drome exterior model
  (used by the drome-exterior screen; only the string is kept by this data layer).

### 4.2 Ten object definitions in file order

`DefineObject` blocks, parsed by the same command reader as everything else (section 5.1). Slots:

| slot | role | LoadTeamFromFile checks |
|---|---|---|
| 0-4 | five Dromers: two starters, the goalie (slot 2), two reserves | type must be `Dromer` |
| 5-8 | four BodyParts (torso/legs per Dromer) | type must be `BodyPart` |
| 9 | the team's Rok model (`Head`) | no explicit check in the original |

The object names and model names are those written in the file (e.g. Chicago: Evans/CHIOMX,
Ballard/CHIDMX, Viveros/CHIGMX (goalie), Leppek/CHISDM, Coffland/CHISOM; four CHIPTM body parts; Rok
Head/CHIROK).

### 4.3 Ratings blocks

Ratings are written as `SetRatings1`/`2`/`3` (skater easy/medium/hard) and
`SetGoalieRatings1`/`2`/`3` plus `SetSpecialGoalieRatings` (goalie special), with
`SetRatings`==`SetRatings1` and `SetGoalieRatings`==`SetGoalieRatings1`.
`Cmd_SetRatings` (HYPERX 0x00483b50, gameengine.cpp lines 2442-2711):

- A skater table has 63 floats (kRatingCount 0x3f, `TeamPlayerRow`), a goalie table 16
  (kGoalieRatingCount). Tables start as all nought (skaters) or all 5.0 (goalies,
  GOALIE_RATING_DEFAULT).
- Each pair is `id_RAT_... = <integer>`; the value is converted with `%d` and stored as a float.
- Values are whole numbers in the shipped files despite the float storage.
- Skater rating order (0-62, identical to DATA/ENUMIDS.H `id_RAT_*`): WeightClass, Stamina, Checking,
  Balance, Speed, ShotPower, ShotAccuracy, PassAccuracy, OffensiveAbility, LongShotEnable, Immunity,
  LongPassEnable, MultiPointSpread, RokHandling, ArmoryRange, Damage, Gender, Speedburst0a..4b (10),
  Bungee0..2, Pass0..2, WinCloseFreeRok, LoseCloseFreeRok, WinFarFreeRok, LoseFarFreeRok,
  WinOffWingJob, LoseOffWingJob, Multiplier0/1, Turnstile0/1, Killball0/1, Randomize0/1,
  Fighting0..4, FlyingRepetoire, HalfpipeRepetoire, CarryRokThrowDist, WinCarryRokRegroupDist,
  LoseCarryRokRegroupDist, WinAggressionOnOthers, LoseAggressionOnOthers, SubOutDamage, SubInDamage,
  MultiRange, MinSubIn. (Index 7, PassAccuracy, is unnamed in `statids.h` because no macro uses it;
  `Cmd_SetRatings` still has it.)
- Goalie rating order: WeightClass, Strength, ReactionTime, Balance, Speed, PassAccuracy, Intelligence,
  UnderPressure, SaveRight, SaveLeft, SaveHigh, SaveLow, ShieldBlocking, CatchingAbility, Aggression,
  Overall.
- An unknown rating name stops that line at the unknown token (the rest of the pairs are not read).
  `EndRatings` is the table's one-past-the-end name and is fatal if given a value; it never appears.
- Old names/typos in the shipped files (read when `DataParseOptions.PlanFixes` is true, which is the
  default and matches hyperblade-20XX; `Original1996` turns them off):
  - `id_RAT_RokReceiving` -> MultiPointSpread
  - `id_RAT_Stealing` -> RokHandling
  - `d_RAT_SubInDamage` -> SubInDamage
  - `id_RAT_Pass` -> Pass2
  With fixes on (hyperblade-20XX gameengine.cpp lines 1372-1406): every EndObjectDefinition fills an
  easy-block SubInDamage left at -1 with 50 and MinSubIn with 40, and the medium/hard blocks with 0.
  These are exactly the shipped defects: NEM's easy blocks omit those two rows, LOSAN has the
  `d_RAT_SubInDamage` typo, JAMCA/NEM/SEATTLE use the old names, and JAMCA writes the special goalie
  block as `SetSpecialRatings` (only read with the fixes on). With fixes off those tokens stop the
  line, leaving the 1996 values (nought).

### 4.4 LoadTeamFromFile mapping (what a match sees)

- Side 0 = home, side 1 = away. Slot 0-4 model ids run `side`, `side+2`, `side+4`, ... (2 apart);
  body parts take ids 94-97 (home) or 98-101 (away) from `g_anBodyPartModelIds`; the Rok slot is
  suppressed to kModelIDUnassigned (-1); reserves keep MODEL_ID_NONE (0x66 = 102).
- Only the first three Dromers are created on the ice (`PlayersOnDrome = 3`): slots 0, 1 and slot 2
  (the goalie); slots 3 and 4 are the bench.
- Away-team definitions get `m_bForceRemap` (palette remap forced).
- Only slots 0, 1, 3 and 4 own a `TeamPlayerRow` (63 floats each). Row order is
  `kRowRosterSlots = {0, 1, 3, 4}`; row 2 is slot 3, row 3 is slot 4. The table is the definition's
  rating block for the engine's current difficulty `m_nDifficulty` (0..2), falling back to easy when
  that block is absent.
- The goalie's 16 ratings are read from the slot-2 definition's goalie tables.

## 5. *.ENV - drome files

Reference: `LoadArena` (HYPERX 0x00484e80, gameengine.cpp lines 2924-2981) and the same object
definition reader. There is **no header or scalar block**: a `.ENV` is a sequence of
`DefineObject` ... `EndObjectDefinition` blocks (0x0a everywhere after the last definition). The only
drome-level scalars are the `Drome` object's own `SetMotionParams` (pos = 0 0 0, fric = .1,
elas = .95 on all 14). The arena collision ellipsoid is hard-coded, not in the file:
`src/engine/arenaellipsoidstatic.cpp` lines 9-17 - semi-axes X=70, Y=125, Z=70 times
`g_flTrackingEllipsoidScale`, friction 0.01, elasticity 0.95; the camera tracker sits 2.2 inside it
(`src/engine/trackingellipsoidstatics.cpp` lines 12-16).

### 5.1 Object definition commands (shared by HTM, ENV and COMMON.CFG)

`ParseObjectDefinition` (HYPERX 0x00482170, gameengine.cpp lines 1340-1446) reads one command per
line; the first token selects one of fourteen commands (case-insensitive):

`DefineObject, EndObjectDefinition, SetMotionParams, SetRatings, SetRatings1, SetRatings2, SetRatings3,
SetGoalieRatings, SetGoalieRatings1, SetGoalieRatings2, SetGoalieRatings3, SetSpecialGoalieRatings,
SetCollisionObject, SetPlans`.

Lines whose first token is none of those are ignored. A recognised command before any `DefineObject`
is fatal. Commands after `EndObjectDefinition` start the next definition.

`DefineObject` attributes (Cmd_DefineObject, HYPERX 0x00482390, lines 1484-1739):
`type`, `modfile`, `name` (all three required), `mat`, and the bare flag `UseMyPallette` (sic, no
`=`). `_` in `name` becomes a space.

- `type` is one of 32 names (id_GOB order): Null, Dromer, Rok, Drome, Ramp, NGoal, SGoal, MainCamera,
  HeadCam, StartGun, Turnstile, Multiplier, LaserHurdle, GreenRandomizer, RedRandomizer, God, SoundMan,
  LimboPole, Hurdle, FastLane, Explosion, Armory, Keyboard, Gamepad, Joystick, Mouse, SlowLane, Mine,
  Swarf, Coach, BodyPart, Generic. `DefineObject::m_nType` is the type index; the object kind
  (EngineObject::m_nKind) folds SGoal->Goal, HeadCam->Camera, SoundMan->Generic.
- `SetMotionParams` (Cmd_SetMotionParams, HYPERX 0x00482b00): keys `pos` (three floats),
  `accel`, `speed`, `azim`, `elev`, `fric`, `elas`. Azimuth/elevation are degrees; positions and
  speeds are original world units (the engine's own scale, not converted anywhere). Missing keys stay
  0.0. Extra tokens after a bad keyword stop that line, so e.g. Turnst4's `pos = -46 80 0 0` reads
  the first three numbers and ignores the trailing 0.
- `SetPlans`: one or more plan names (whitespace separated) in order; resolved against PLANS.CAT
  when the plan binary is loaded.
- `SetCollisionObject` (Cmd_SetCollisionObject, HYPERX 0x00483300, lines 2166-2433): attributes
  `type` (required), `component` (%hd), `radius` (%f), `centroid` (three floats, required),
  `attachPoint` (three floats), and the bare flag `useDetail`. 27 bubble keywords:
  DromerAwareness, DromerModel, DromerBody, DromerScoop (attached shape), Ramp (caged shape), Rok,
  GoalSide, GoalZone, GoalCase, GoalPort, Camera, HeadCam, Multiplier, Turnstile, Startgun,
  LaserHurdle, Armory, LimboPole, FastLane, Explosion, Hurdle, Randomizer, "MAJOR KLUDGE HERE",
  SlowLane, Mine, Swarf, BodyPart (all other shapes are spheres). A `RandomizerBubble` on a
  RedRandomizer object becomes the kludge kind. The bubbles are chained: each new command becomes the
  child of the previous tail, and each parent's child count increments.
  Quirks kept: the "radius missing" test in the original compares the -1 default against a sentinel
  that can never be -1, so a missing radius silently stays -1 (the loader never notices); the centroid
  is mandatory; `useDetail` is only stored when there is no `attachPoint` (attach wins).
  The parsed bubble holds the pre-map slot index; `MapModifierToId` (HYPERX 0x00484070, lines
  2716-2825) turns it into an `id_COB_*` value using the object's model id at creation time, which
  belongs to the object/simulation layer.
- `EndObjectDefinition` closes the definition and applies the PlanFixes fill described in 4.3.

### 5.2 Shipped drome inventory

Every `.ENV` has exactly: 1 Drome named `king` (model named after the drome), 1 Rok `AndRoll`
(model `Rokmix`), 1 NGoal `goal0` at pos (0, 192.5), 1 SGoal `goal1` at (0, -192.5), 1 StartGun,
1 God `inscrut`, 1 SoundMan `Rob`, 2 Coach (`Coach0`, `Coach1`), 1 Explosion, 1 Mine, 1 Swarf and
1 RedRandomizer `RRandom`; the rest varies:

| drome file | objects | notable |
|---|---|---|
| chidro.env | 29 | 2 ramps, 4 turnstiles, 4 green randomizers |
| houdro.env | 30 | 4 ramps, 4 turnstiles, 2 armories |
| jamdro.env | 44 | 4 ramps, 6 turnstiles, 6 laser hurdles, 8 green randomizers |
| lnddro.env | 32 | 4 laser hurdles, 3 green randomizers, 2 armories |
| losdro.env | 35 | 2 ramps, 6 turnstiles, 2 laser hurdles, 2 armories |
| mosdro.env | 31 | 4 turnstiles, 2 laser hurdles, 2 armories |
| mundro.env | 37 | 4 ramps, 6 turnstiles, 2 laser hurdles, 6 green randomizers |
| nemdro.env | 35 | 2 ramps, 6 turnstiles, 6 laser hurdles, no green randomizer |
| newdro.env | 29 | 6 turnstiles, 4 green randomizers, 1 armory |
| seadro.env | 39 | 4 ramps, 6 turnstiles, 8 green randomizers, 2 armories |
| tokdro.env | 33 | 2 ramps, 4 turnstiles, 4 laser hurdles, 2 armories |
| tordro.env | 27 | 2 ramps, 4 turnstiles, 4 hurdles, 1 armory |
| vegdro.env | 32 | 2 ramps, 6 turnstiles, 8 green randomizers, 1 multiplier only |
| wardro.env | 31 | 1 ramp, 4 turnstiles, 4 laser hurdles, 8 green randomizers |

(Variants are the file's own commented-out `#DefineObject` blocks; only uncommented blocks load.)

## 6. GAME.CFG

`GameEngine::LoadGameConfig` (HYPERX 0x00481e50, gameengine.cpp lines 1227-1310). Three keywords only:
`Environment`, `HomeTeam`, `AwayTeam` (`key = value`); every other line is ignored. A keyword with no
value is a "Badly formed Game Config file" error. Shipped:
`HomeTeam = LOSAN.HTM`, `AwayTeam = SEATTLE.HTM`, `Environment = HOUDRO.ENV` (names are resolved
against TEAMS.CAT / the Data folder case-insensitively).

## 7. COMMON.CFG

Loaded with the object-definition reader (`LoadObjectsFromConfig`, HYPERX 0x00484cf0, gameengine.cpp
lines 2892-2918; called from `GameEngine::Init` line 4176). Shipped: 7 definitions -
`MainCamera mainCam` (modfile=null), `HeadCam headCam` (null), `Keyboard keyboard` (null) and four
`Explosion` models Partcl1..Partcl4. Plans: Camera Sausage minimal / HelmCam minimal / input /
Explode.

## 8. BOOIE.INI

Read through `GameOptions` (`src/engine/gameoptions.cpp`, constructor HYPERX 0x00479880, getters
0x004799a0-0x00479ab0, `ReadInt` 0x00479960), i.e. Win32 `GetPrivateProfileIntA` semantics:
section/key names case-insensitive, missing file/section/key -> caller default, a found value is
trimmed and converted like `atoi` (no digits -> 0), and a negative integer reads as 0. Whole-line
`;`/`#` comments only; no inline comments. hyperblade-20XX's `common/inifile.cpp` is the reference
implementation.

| section | key | default | shipped |
|---|---|---|---|
| Options | Difficulty | 1 | 0 |
| Options | Brutality | 1 | (absent -> 1) |
| Options | SfxVolume | 7 | 7 |
| Options | MusicVolume | 4 | (absent -> 4) |
| Options | ScreenSize | 6 | 6 |
| Options | PeriodLength (ms) | 0x493e0 = 300000 | (absent -> 300000) |
| Options | AttackMode | 0 | 0 |
| Options | TeammateMode | 0 | 0 |
| Options | NetNoVerify | 0 | (absent) |
| Options | ModemPlay | 0 | (absent) |
| Misc | InstallLevel | 0 | 2 |
| Misc | DromeTextured | 1 (DROME_TEXTURED_DEFAULT, src/graphics/renderer.cpp line 45) | 1 |
| Misc | Potatoe | 0 | (absent) |
| Keys | Forward, Stop, RotateLeft, RotateRight, Attack, SpeedBurst, Shoot, Pass, QuickShot, Jump | VK_UP, VK_DOWN, VK_LEFT, VK_RIGHT, 'S', 'D', VK_SPACE, 'C', 'F', 'E' = 38,40,37,39,83,68,32,67,70,69 (`g_anKeyBindingDefaults`, src/input/keyboarddeviceobj.cpp lines 610-622) | all ten present |
| Gamepad | Attack, Speed, Shoot, Pass | i+1 (`ReadInt("Gamepad", name, i+1)-1` is stored, src/input/joystickmgr.cpp line 270) | 1,3,4,2 |

The `[Keys]` values are the codes the game actually uses for play (`LoadBindingsFromIni`, 0x0045d280);
the DFT files (next section) are the key-config-dialog presets, not the play bindings.

## 9. PREFS.TXT and DFTKEYBD / DFTMOUSE / DFTSTICK

`Config::LoadPreferences` (HYPERX 0x0046d710, src/app/config.cpp lines 101-132) reads one
whitespace-delimited token (`fscanf("%s")`) from `Prefs.txt`, which is the path of the active key
preset; a missing/unreadable file falls back to `Data\DftKeybd.cfg`. Shipped `prefs.txt` contains
`Data\DftStick.cfg`.

`Config::ParseFile` (HYPERX 0x0046d8b0, lines 136-186) reads the preset with `fscanf`, not lines:

    for (row = 0; row < 40; row++) fscanf(file, "%d %d %d: ", &flags, &key, &index);
    fscanf(file, " %f %f %f %f", &forceIncrement, &maxAcceleration, &maxSpeed, &forceRelaxation);
    fscanf(file, "  @v02 %d %f %f %f %f %f :",
           &victimCam, &bandwidth, &netTracking, &stopForceFactor, &speedBurstFactor, &viewSteepness.z);
    four times: fscanf(file, " %f %f %f :", &trackOffset.x/y/z);

A `KeyRow` is `{ unsigned char flags; unsigned char unused; unsigned short key; unsigned short index; }`
(`src/globals.h` lines 303-309). The shipped files hold fewer than 40 complete rows, so the remaining
row reads keep the last converted values (C leaves a failed conversion's variable untouched).
hyperblade-20XX documents and reproduces exactly this in `src/app/config.cpp` lines 133-147.

Exact parse of the shipped presets (verified byte-by-byte; `CompleteRows` in the loader):

| file | complete rows | rows 0 / 37 / 38 / 39 | 4 steering floats | @v02 victim; 5 floats | track 1 |
|---|---|---|---|---|---|
| DftKeybd.cfg | 38 of 40 | 1 38 0 / 5 13 4 / 0 13 4 / 0 13 4 | 0.8, 1, 1, 0.8 | 0; 0.8, 1.25, 1, 0.9, 1 | (1.2, 2, 0) |
| DftMouse.cfg | 30 of 40 | 1 0 63 / 1 13 4 / 1 13 4 / 1 13 4 | 0, 1, 0, 1 | 0; 1, 1, 1, 2, 1 | (1, 2, 0) |
| DftStick.cfg | 40 of 40 | 1 38 0 / 5 0 58 / 9 66 23 / 5 66 23 | 1, 2.3, 0.7, 1 | 0; 1, 1, 1, 0.8, 2 | (0, 0, 0) |

Consequences worth keeping: the four steering globals start at 1.0 in `config.cpp` lines 78-89; the
`@v02` line in DftStick has two extra floats before its colon, so the call converts the first five
floats, fails on the literal colon, and the four `float float float :` calls that follow read the
remaining "0.000000 0.000000 :" and leave every track at (0,0,0). `DftKeybd`'s "0.<line break>800000"
is irrelevant to the token reader. These values are what the 1996 executable loads, even though they
were never authored.

`g_aDefaultKeyBindings` (src/app/config.cpp lines 305-307) is the forty-row fallback used by the key
dialog when the preset table is empty; it is reproduced in `KeyConfigFile.CreateDefaultRows()`.
The DFT rows' `index` values beyond the ten action ids (58, 63) are key-dialog binding ids whose table
is not recovered here (see section 18).

## 10. PLANS.CAT

`GameSession::GetPlansEntry` (HYPERX 0x00441f60, src/net/gamesession.cpp lines 862-912) tokenises each
line up to whitespace or '.', skips `#` lines, truncates the stored name to 15 characters
(`SCAT_NAME_MAX`). Shipped: 32 plans (ARMORY..TURNSTIL), matching the 32 `plans\*.pbn` files. Plan
names inside HTM/ENV are matched case-insensitively against these stems; the binaries themselves are
loaded by `PlanFile::OpenBinary` from `plans\<name>.pbn` and are not parsed by this data layer.

## 11. MODEL.LST and ANIMATE.LST

Ordered name lists, one per line. **No loader for either file exists** in the byte-matching
decompilation (searched the whole decomp tree) and hyperblade-20XX does not read them either; they are
authoring/asset-build lists. UBlade3 preserves them verbatim:

- `MODEL.LST` - 26 names: chio, JAMdrome, vegdrome, NYdrome, houso, jamo, lao, lono, moso, muno,
  nemo, nyo, seao, toko, toro, waro, offense, offense2..5, vegmno, ladrome, ramp0, goalStub, rok.
- `ANIMATE.LST` - 97 animation names (apgthov2 ... apgspfb2) that match the `animdata\*.wba` clips.

The actual model files live in `data\models\mdlshd` and `data\models\mdltxt` (201 files each:
.wbm meshes and .wbp palettes). `modfile` values in HTM/ENV are model names without extension;
`ModelFile::Open` (src/graphics/modelfile.cpp lines 25-87) appends the extension and uses
`Models\Mdltxt` or `Models\Mdlshd` (shaded drome models). "null" means no model. UBlade3 exposes
`ModelLibrary` (173 unique stems across both folders) and reports unresolved model names; on the
shipped data every non-null reference resolves.

## 12. SNDFILES.LST

`SoundManager::Load` (HYPERX 0x00449280, src/sound/soundmanager.cpp lines 61-183).
- The first non-comment line is a header scanned with `sscanf("%d %s")`: `22 kHzAnnouncer` (sample
  rate 22, name "kHzAnnouncer"). Neither value is used by the loader.
- `m_nSoundCount = (non-comment lines) - 1`; the walk stops at the first zero-length line.
- Each later line is `sscanf("%s %d %d %d", name, &durationMs, &group, &flag)`. Fewer conversions
  keep the previous values (the C variables persist); `flag` is the load mode only when four fields
  convert. Shipped: 363 entries, matching the declared count; durations are milliseconds, with 0
  meaning "unknown" for many entries; groups are 0/1/2 for real SoundTypes and 666/777/999 for
  "no group".
- The fifteen `SoundTypes` groups get first index, count and the group's longest duration scaled by
  `SAMPLES_PER_MS = 44.1` (`(int)(longest * 44.1)`). A group id outside 0..14 is stored per entry but
  does not open a group; durations from such entries are still folded into the previous group's
  `nLongest` (an original quirk kept). Shipped group 1: first index 4, 5 entries, 88155 samples.
- The 295 `sounds\*.wav` files are loaded by the sound layer; this data layer only parses the list.

## 13. INFOBTXT.LST

`MessageQueue::LoadList` (HYPERX 0x00452ba0, src/ui/messagequeue.cpp lines 190-271).
Line format: `<identifier> <colour> <message text>`. The identifier is discarded; entries are found
by their position (the file comments say "Text displayed by CPP code (Starts at 79)", i.e. entry
index = message id). `colour` is `atoi` of the second field (0 green, 1 blue, 2 red). The message is
everything after the second space, verbatim - including the extra leading space produced by the
shipped double spaces (`Injury 1  Injury! ` -> text " Injury! "). `#` lines are comments. A line with
no space becomes the original's "!EEK empty description on line: N" text with id 0; a missing message
becomes "!EEK no string on line: N". Shipped: 96 entries.

## 14. JUMBO.TXT

`JumboTextList::Load` (HYPERX 0x0046c010, src/ui/jumbotextlist.cpp lines 60-117): every non-comment,
non-empty line is one advert name, verbatim and in order. Shipped: 17 names (BLAMLT ... SUPERQ).

## 15. HITANIMS.LST / CHKANIMS.LST / JMPANIMS.LST / THRANIMS.LST

`PlanVM::InitReactionTables` (src/planvm/planvm.cpp lines 389-456). Each file has exactly 5 lines of
7 tab-separated animation names (`sscanf(line, "%s\t%s\t%s\t%s\t%s\t%s\t%s")`); any line that does not
yield seven names is fatal ("Error reading Animation List file"). The names are resolved against the
animation registry at run time; this data layer keeps the 5x7 name tables for each file.

## 16. GAUNTLET.CFG

`SaveObject::ParseGauntletConfig` (HYPERX 0x00448ee0, src/ui/saveobject.cpp lines 128-181).
`m_aBracket[20][20]` starts as all -1 (`memset(..., -1)` in the constructor, lines 20-28). Each line
starting with a character other than '#' is a home team: the team label before the first space, then
up to twenty space-separated team indices (`atoi` after each space). A line with no space is the only
parse error ("Gauntlet Error--Opponent Team errors on line: N"). Shipped: 14 rounds, and the last two
values of each line (2 and 8) are the bracket's hidden-team slots, matching
`HIDDEN_TEAM_A=8`/`HIDDEN_TEAM_B=2` (gameengine.cpp lines 325-326) used by the bracket iterator's
`WrapToNextValidTeam`.

## 17. ENUMIDS.H and the other asset folders (not data model)

`data\enumids.h` is the original C header that numbers `id_GOB_*`, `id_RAT_*`, `id_GRT_*`, model ids
and animation ids; it is reference material, not a runtime data file, and is not parsed (20XX only
uses it for stat-name lookup in its simulator). Asset folders are likewise outside this layer:
`animdata\*.wba`, `assets8\*.bmp`, `models\mdlshd|mdltxt\*.wbm|*.wbp`, `plans\*.pbn`,
`sounds\*.wav`, `textures\*.bmp`, `palette.gif`, `custom.pal`. The importer layer will read those.

## 18. Unresolved fields and deliberately unported pieces

- `MODEL.LST` / `ANIMATE.LST` consumers: no loader found in the decompilation or 20XX. Kept as ordered
  names; their original consumer is unknown.
- `dromext` (e.g. "chiext"): used by the drome-exterior screen to compose its model name; the naming
  convention beyond the string is not recovered here.
- Collision `id_COB_*` ids: the text parser stores the pre-map bubble kind and the creation-order model
  id; applying `MapModifierToId` (gameengine.cpp lines 2716-2825) belongs to the object-creation layer
  and is not written into the data objects yet.
- Plan binaries (`plans\*.pbn`) and plan text: only PLANS.CAT names are parsed here.
- Sound playback and WAV loading: SNDFILES.LST is parsed, the WAV estate is not.
- `TeamsBlock::ComputeTeamNameChecksum` (teamsblock.cpp lines 131-161; folds lowercased names) is
  documented but not implemented - the original uses it for network asset matching only.
- DFT key-row `index` values above the ten action ids (58, 63) are key-dialog binding ids whose action
  table was not recovered; the raw rows are preserved.
- The key-config preset writer (`Config::SavePreferences`, 0x0046d3a0) is an original stub that writes
  a never-filled buffer; no write path is ported (the data layer is read-only).
- All object `m_cUnused`/`m_wUnused`, `TeamsBlock` and `ArenaState` unused members are zero and have
  no data representation.
- Ratings that the loader supports but the shipped files never use: Bungee0..2, Killball0/1,
  Randomize0/1, id_RAT_PassAccuracy as a written key, `EndRatings`.
- Line lengths: the 0x100-character `GetNextLine` cap is implemented but the shipped HTM/ENV files
  never reach it (longest is seattle.htm at 155 characters).
- The files are ASCII-only; a modded file with high bytes is decoded byte-for-byte (Latin-1), the way
  the Windows ANSI code page behaves for these files, not through a Unicode guess.

## 19. Verification record

Anchor values for the shipped disc (14 teams, 14 dromes). The `DataProbe` report is:

    UBLADE3_DATA_OK teams=14 dromes=14 digest=0135846dd22b3e3d
    UBLADE3_DATA_COUNTS teams=14 dromes=14 teamObjects=140 dromeObjects=464 plans=32 models=26
      modelsLibrary=173 animates=97 sounds=363 soundsDeclared=363 info=96 jumbo=17 anims=5x7
      keyRows=40 modelRefs=552 planRefs=836
    UBLADE3_DATA_DIGEST teams=b366aac704a622b1 dromes=f4c2a1cf6b62c04e
      catalogs=d378eb6feaa44140 configs=e3d48b9086cc56b5 total=0135846dd22b3e3d
    UBLADE3_DATA_WARNINGS 0

The digest is FNV-1a 64 over a canonical text stream built from the parsed values (teams, dromes,
catalogues, configs). It therefore changes if the user's install (or a local BOOIE.INI/DFT edit)
changes; the teams/dromes/catalogs sub-digests are stable for the retail disc. Configuration edits
are expected to change `configs` and `total` only.

Verification performed while building this layer:
- Two independent netstandard2.1/Unity-compatible compiles (dotnet SDK with LangVersion 9; the Unity
  project compiles the same files, and the Lead ran `DataProbe` in Unity against the full install).
- An independent reference reader (written separately from the C# layer, straight from the decomp and
  the raw files) reproduced 361 exact value lines covering: all 14 team headers, the five Dromer
  definitions per team (name/model/pos/azim/elev/plans/easy+hard Damage/MultiPointSpread), goalie
  Intelligence at all four levels, the four outfield rows, slot model ids and on-ice flags, body
  parts and Rok; all 14 drome object counts, Drome/Rok/goal names-models-positions and every
  Ramp/Multiplier/Turnstile placement and collision count; all three DFT presets (complete rows,
  rows 0/37/38/39, steering floats, camera floats, partial track reads) and the active preset from
  PREFS.TXT; SNDFILES header/count/group data; INFOBTXT entries and exact message text; JUMBO,
  PLANS, MODEL, ANIMATE, the four animation tables, GAME.CFG, all 14 Gauntlet rounds, COMMON.CFG, and
  the full collision chain of Chicago's first Dromer. The diff was 0 mismatches.
- `DataProbe` also cross-checks every model and plan name against the model folders and PLANS.CAT;
  zero warnings remain on the retail data.

## 20. C# layer map (all under `Assets/Scripts/UBlade3/Data/`)

| file | contents | decomp references |
|---|---|---|
| `ParseUtil.cs` | DataFormatException, FindKeyword, ParseToken, SkipToChar/PastChar, scanf number helpers, name spacers, Latin-1 decode | src/common/parseutil.cpp (0x00435f90, 0x00487370, 0x00487420, 0x00487460) |
| `DataTextFile.cs` | FileObj::GetNextLine semantics + physical line numbers | src/common/fileobj.cpp lines 169-220 (0x004406b0) |
| `GameDataRoot.cs` | install/data resolution, case-insensitive file index, required-file validation, model library | port contract; src/graphics/modelfile.cpp |
| `IniFile.cs` | Win32-profile INI reader and writer-free document | src/engine/gameoptions.cpp; 20XX common/inifile.cpp |
| `BooieConfig.cs` | BOOIE.INI sections/keys/defaults, [Keys]/[Gamepad] tables | gameoptions.cpp, gameengine.cpp 437-450, keyboarddeviceobj.cpp 610-622, joystickmgr.cpp 268-272, renderer.cpp 45/73, saveobject.cpp 183-184 |
| `KeyConfigFile.cs` | PREFS.TXT + DFT*.CFG fscanf parser, default key table | src/app/config.cpp 101-186 and 305-307; 20XX src/app/config.cpp 133-147 |
| `ObjectDefinition.cs` | object/rating/bubble models and all name/id tables | objectdef.h, statids.h, objectkind.h, enumids.h, bubblecmd.h, gameengine.cpp 1509-1578/2182-2237/2444-2588 |
| `ObjectDefinitionReader.cs` | ParseObjectDefinition + DefineObject/motion/ratings/collision/plans, PlanFixes rules | gameengine.cpp 1340-1446, 1484-1739, 1757-1882, 2166-2433, 2442-2711, 2832-2856; 20XX 1283-1294, 1372-1406, 2666-2683 |
| `TeamFile.cs` | TEAMS.CAT, DefineTeam header, ten slots, LoadTeamFromFile layout/rows | teamsblock.cpp 36-126; gameengine.cpp 1945-2161, 3210-3403, 5590-5601; teamblock.h |
| `DromeFile.cs` | ENV object list, drome/rok/goal lookups, object-count validation | gameengine.cpp 2924-2981; arenaellipsoidstatic.cpp |
| `GameConfigFile.cs` | GAME.CFG | gameengine.cpp 1227-1310 |
| `GauntletConfig.cs` | GAUNTLET.CFG bracket | saveobject.cpp 20-28, 128-181; gameengine.cpp 325-326 |
| `Catalogs.cs` | PLANS.CAT, SNDFILES.LST + group table, INFOBTXT.LST, JUMBO.TXT, MODEL.LST/ANIMATE.LST | gamesession.cpp 862-912; soundmanager.cpp 61-183; messagequeue.cpp 190-271; jumbotextlist.cpp 60-117 |
| `AnimationLists.cs` | HIT/CHK/JMP/THR anim tables | planvm.cpp 389-456 |
| `GameData.cs` | aggregate load + cross-reference validation | loaders above |
| `DataProbe.cs` | scene-free batch-mode gate: `Describe()`, `Check()`, `Run()`, digest | n/a |

Units are the original's throughout: positions/speeds/radii in the engine's world units, azim/elev in
degrees, SNDFILES durations in milliseconds, BOOIE period length in milliseconds, DFT steering/camera
offsets in the original's internal units. No conversion is applied by this layer.
