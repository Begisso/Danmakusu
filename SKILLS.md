# Skills

Repeatable procedures for working on Danmakusu. Paths are relative to this folder unless they start with a drive.

## Build and deploy

1. `dotnet build`. The build closes osu! (asks first, kills it after 5 s) and copies the dll to `%APPDATA%\osu\rulesets` and to `bin\Release\net10.0`. The build output itself stays in `bin\Debug\net10.0`, which the tools reference.
2. Every build also zips the project (sources, docs, resources; not bin, obj or .claude) and the built dll into `..\Archives\Danmakusu-<date>-<time>.zip`; the newest 30 are kept. The latest is also always at `..\Archives\osu.Game.Rulesets.Danmakusu.zip`, and the ruleset dll itself at `..\Archives\osu.Game.Rulesets.Danmakusu.dll` (both replaced by every build). This is the only history besides HISTORY.md (not a git repo): restore by unzipping. `-p:Archive=false` skips it.
3. `dotnet build -p:CloseOsu=false` only checks that it compiles while osu! is running (the copy then warns).
4. After every change: an entry in `HISTORY.md` (per boss, per date, old → new values), and the boss's line in `Bosses/BossDescriptions.cs` when its behaviour changed.

## When osu! updates

A ruleset built for an older osu! loads, but a call to anything that has changed in the new one is a `MissingMethodException` when it is reached (2026-11-18: every map crashed as it loaded, `AudioManager.GetSampleStore`). The log says where (`%APPDATA%\osu\logs\*.runtime.log`).

1. The installed version: `(Get-Item "$env:LOCALAPPDATA\osulazer\current\osu.Game.dll").VersionInfo.ProductVersion`. On NuGet: `https://api.nuget.org/v3-flatcontainer/ppy.osu.game/index.json` and `.../ppy.osu.framework/index.json`.
2. In the csproj of the ruleset, `..\Tools\SectionCheck` and `..\Tools\LazerImport`: `ppy.osu.Framework` at the installed version, the three game packages at the installed version if NuGet has it, otherwise the newest there is. `UseInstalledLazer` compiles against the installed lazer's `osu.Game*.dll` either way.
3. `dotnet build -p:CloseOsu=false -p:Archive=false`, then `..\Tools\RefCheck\bin\Release\net10.0\refcheck.exe bin\Debug\net10.0\osu.Game.Rulesets.Danmakusu.dll "$env:LOCALAPPDATA\osulazer\current" "$env:USERPROFILE\.nuget\packages\lib.harmony\2.4.3\lib\net10.0\0Harmony.dll"`: 0 missing. (On the old dll it shows what the update broke.)
4. The patches: the game's log at start says one line for each (`Danmakusu: ...`), or why it could not; `sectioncheck sortstars` and `sectioncheck offered` on the new SectionCheck. Names looked up by reflection elsewhere (`MapColours`, `BackgroundColour`, `DrawableDanmakusuRuleset`, `DanmakusuModColumn`'s statics): `refcheck names <lazer folder> "ModSelectOverlay::customisationPanel" ...`.
5. Rebuild the watcher's copies: `dotnet build -c Release -o bin\Watch` in `..\Tools\LazerImport`, `dotnet build -c Release -o bin\Stars` in `..\Tools\SectionCheck`; `lazerimport --report` reads a copy of the database (the new osu! must have started once). Then `dotnet build` to deploy, and a map played in osu!.

## Benchmark the field (freezes, crashes)

The `field` mode of `..\Tools\SectionCheck` runs the real `BulletField.Update` headlessly (no audio, no skin, no GPU) at 60 frames per second, with a 16:9 screen round the playfield and both bosses in every section (Always Double Boss).

1. Build the ruleset, then the tool: `dotnet build ..\Tools\SectionCheck -c Release` (it copies the ruleset dll at build time: rebuild it after every ruleset build).
2. Run `..\Tools\SectionCheck\bin\Release\net10.0\win-x64\sectioncheck.exe field "<map.osu>" <from m:ss> <seconds> <what>`:
   - `singles`: every modified boss alone;
   - `pairs`: every pair of modified bosses (820 pairs: hours); `pairs:a,b,c`: every pair of those;
   - `a+b,c+d`: those combinations. Ids can be shortened to their start (`reisen` = `reisen_udongein_inaba`).
3. Each line gives the average, 99th percentile and worst ms per frame, allocations, planning time, and the four steps of `Update` that take the longest (`BulletField.PROFILE_STEPS`). Errors (crashes) are printed instead of times. `BENCH_SHORT=1` prints only the three times and the two costliest steps (use it when only the speed matters: it keeps the output small). A list of more than three bosses (a whole game's roster from a log) is named by its first two and how many more.
4. Heavy test spot: Camellia - Chirality [Asymmetry] from 3:00 for 15 s. Budget: about 4 ms per frame for the field (a 144 Hz frame is 6.9 ms in all).
5. Not measured: drawing on the GPU, and the copies of circles and sliders (Doremy, Sagume) that osu! updates itself.

The older `bench` mode only times ring positions (`BulletEmitter.Evaluate`), without stages, collisions or the renderer.

## Tune the boss sections

1. Add the user's example map to `..\Tools\SectionCheck\maps.ps1` (folder, audio, wanted sections, difficulties; `~m:ss-m:ss` = optional range).
2. `$a = & .\maps.ps1` in the tool folder, then:
   - `auc @a`: how well each measure separates wanted from unwanted on every map;
   - `eval "<weights>" "2/0.9" @a`: what one weight set picks (weights in `Search.MEASURES` order, holes/hole share);
   - `climb "-2,-1,0,1,2,3,4" 400 @a`: hill climbing over all measures.
3. Tune on every example map at once, never on one alone, or the others break. Put the chosen weights into `DanmakusuBalance` (`BOSS_FILL_*`) and record old → new values in `HISTORY.md` (Boss sections).
4. Before believing a climb, `eval` its weights on `$a = & .\maps.ps1 -All` too (the difficulties it did not see): a climb on the short list overfits badly. To climb from the current weights instead of random ones, set the environment variable `CLIMB_SEED` to them. The main mode (`sectioncheck "<map.osu>" "<audio>" out.csv "<targets>"`) shows what the game really picks, with the map's own sections, and its csv has every measure per stretch: compare the wanted and unwanted parts there to see whether any measure can tell them apart at all.

## Boss list spreadsheet

`Touhou bosses.xlsx` in `%USERPROFILE%\Downloads\Danmakusu`: made by the `bosslist` C# tool (ClosedXML, `..\Tools\BossList`; the ideas of the bosses not built yet are its `ideas.txt`, one line each: name, basic attack, mechanics, separated by tabs) from the built dll. Run it from that folder with `dotnet run -- "<this folder>\Bosses\BossMechanic.cs" "%USERPROFILE%\Downloads\Danmakusu\Touhou bosses.xlsx"`. Modified bosses are green and show their in-game description; the others show their planned mechanic. Regenerate it after boss changes. A third argument, `%USERPROFILE%\Downloads\Danmakusu\Touhou_Boss_Abilities.xlsx`, updates the user's ability sheet in place (every built boss's row gets a green background with the default text colour, and column C becomes `Built: <in-game text>`; a `.bak` is kept); do this after every build too.

## Images and GIFs

There is no Python on this machine. Use a small C# console tool with SixLabors.ImageSharp (3.1.11 is in the NuGet cache, so it works offline): cut frames, centre them, scale them and save them as PNG under `Resources/Textures` (embedded resources).

## New characters (placeholders)

1. The art goes into `old builds and assets\Touhou Characters` as `<Name>.<ext>` (found by name). Art on white: cut it out first with `Bake cutout <in> <out.png> <its own longest side> <preview.png>` (keep its size: a bigger number enlarges it), and put the cut-out PNG there instead. Look at every cut-out on dark and light grey: white clothes and hair must stay.
2. The yellow scribbles of ZUN's art (a line of Japanese beside the head) come out of the art before anything is made from it (the old pictures have none): find each with `..\Tools\BossPictures` `grid`, then `inpaint <art> <art> <box> 6` and `clearsoft` on a slightly bigger box; a letter over yellow hair needs `inpaint ... 3 strict` on its own small box, and a stub over the background `clear`. Check with `crop` and `sheet`.
3. `bosspictures sprite <art> Resources\Bosses\Sprites\<id>.png` (172 x 193) and `bosspictures crop <art> Resources\Bosses\Heads\<id>.png <x> <y> <size>` (96 x 96: the whole head and a little room above it, as Zanmu's) - put the heads of the new ones next to an old one in a `sheet` before copying them in.
4. `..\Tools\Portraits` with the art folder, the sprites, the portraits and the new ids: the big pictures (`Resources\Bosses\Portraits\<id>.webp`).
5. `Bosses\BossRoster.cs`: one `make` each, by id in order (ordinal), the indices renumbered, the old entries' templates kept; the colours by eye (eye, hair, main clothes, second clothes or tool). A placeholder needs nothing else: one star and the placeholder text come by themselves.
6. The ideas: a line each in `..\Tools\BossList\ideas.txt` (name, basic attack, mechanics, tab-separated, ending with the character it comes from - look the character up: their ability, species, story, spell cards). The user's ability sheet gets their rows with `bosslist addrows <Touhou_Boss_Abilities.xlsx> <name> <lore> ...`; then the usual run of the tool fills their ideas, and makes the boss list.
