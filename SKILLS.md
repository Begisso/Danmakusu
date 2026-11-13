# Skills

Repeatable procedures for working on Danmakusu. Paths are relative to this folder unless they start with a drive.

## Build and deploy

1. `dotnet build`. The build closes osu! (asks first, kills it after 5 s) and copies the dll to `%APPDATA%\osu\rulesets` and to `bin\Release\net10.0`. The build output itself stays in `bin\Debug\net10.0`, which the tools reference.
2. Every build also zips the project (sources, docs, resources; not bin, obj or .claude) and the built dll into `..\Archives\Danmakusu-<date>-<time>.zip`; the newest 30 are kept. The latest is also always at `..\Archives\osu.Game.Rulesets.Danmakusu.zip`, and the ruleset dll itself at `..\Archives\osu.Game.Rulesets.Danmakusu.dll` (both replaced by every build). This is the only history besides HISTORY.md (not a git repo): restore by unzipping. `-p:Archive=false` skips it.
3. `dotnet build -p:CloseOsu=false` only checks that it compiles while osu! is running (the copy then warns).
4. After every change: an entry in `HISTORY.md` (per boss, per date, old → new values), and the boss's line in `Bosses/BossDescriptions.cs` when its behaviour changed.

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

## Retrain the map generator

The Map Generation mod's model is learnt from the downloaded maps and calibrated against the game's own star rating, so retrain it whenever the star rating changes (`StarRating`, `STAR_*`, `DENSITY_*`, `FORCED_*`) or the star list grows.

1. Build the ruleset and `..\Tools\SectionCheck` (Release).
2. `sectioncheck trainmap "H:\osu maps\osu files" "H:\osu maps\danmakusu_stars.csv" Resources\Generation\map_model.json` (about 5 minutes on every core). Every level's line also shows what is learnt of the maps' structure: how far apart the spacing of a combo is, how much its turns differ, how many combos zigzag, how often a bar's rhythm comes again, how often a combo starts on a bar's first beat.
3. `sectioncheck gentest Resources\Generation\map_model.json calibrate "H:\osu maps\osu files" 30 "H:\osu maps" <scratch folder>` (about 2-3 minutes, 16 sources; the songs are taken out of the .osz files): writes `MeanStars` and `SourceFor` into the model. Below source 1 maps are drawn further apart, past the last closer together.
4. `sectioncheck gentest Resources\Generation\map_model.json check "H:\osu maps\osu files" 40 "H:\osu maps" <scratch folder>`: the levels as the game makes them; the medians should be within about 0.1 of each level from 2 to 10.
5. Build again (the model is embedded). `genmap` writes a generated map to look at in osu!'s editor.

## Boss list spreadsheet

`Touhou bosses.xlsx` in `%USERPROFILE%\Downloads\Danmakusu`: made by the `bosslist` C# tool (ClosedXML, `..\Tools\BossList`; the ideas of the bosses not built yet are its `ideas.txt`, one line each: name, basic attack, mechanics, separated by tabs) from the built dll. Run it from that folder with `dotnet run -- "<this folder>\Bosses\BossMechanic.cs" "%USERPROFILE%\Downloads\Danmakusu\Touhou bosses.xlsx"`. Modified bosses are green and show their in-game description; the others show their planned mechanic. Regenerate it after boss changes. A third argument, `%USERPROFILE%\Downloads\Danmakusu\Touhou_Boss_Abilities.xlsx`, updates the user's ability sheet in place (every built boss's row gets a green background with the default text colour, and column C becomes `Built: <in-game text>`; a `.bak` is kept); do this after every build too.

## Images and GIFs

There is no Python on this machine. Use a small C# console tool with SixLabors.ImageSharp (3.1.11 is in the NuGet cache, so it works offline): cut frames, centre them, scale them and save them as PNG under `Resources/Textures` (embedded resources).
