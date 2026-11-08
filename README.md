# Danmakusu

A fan-made ruleset for osu! (lazer): osu!standard maps played as a bullet hell. The circles, sliders and spinners play themselves and fire bullets and laser beams to the music, and you dodge them. Bosses, Touhou Project characters, take over the map's breaks, kiai sections and spinners, each with an attack of their own.

- Every osu!standard map works, and maps of mania, taiko and catch are converted too.
- Choose how many bosses a map has with the Boss Selection mods (one, two or three bosses at once).
- Song select shows Danmakusu's own star rating and counts (bullets, beams, the most bosses a map has room for), and can sort by them.

## Install

1. Download `osu.Game.Rulesets.Danmakusu.dll` from the [latest release](../../releases/latest).
2. In osu!, open Settings → General → "Open osu! folder", and put the dll in its `rulesets` folder (on Windows: `%APPDATA%\osu\rulesets`).
3. Restart osu!. Danmakusu appears next to the other modes.

The dll is built for osu!(lazer) **2026.916.0**. Another version of osu! may refuse to load it, or crash with it.

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
```

The dll ends up in `bin\Debug\net10.0`. If osu! is installed, the build also copies it into osu!'s `rulesets` folder, and **closes osu! first** (osu! keeps the old dll locked while it runs). To leave osu! running, build with:

```
dotnet build -p:CloseOsu=false
```

To build for another osu! version, change the version of `ppy.osu.Game` and `ppy.osu.Game.Rulesets.Osu` in `osu.Game.Rulesets.Danmakusu.csproj` to that version (osu! → Settings → About shows it).

## Disclaimer

This is a non-commercial fan work. It is not affiliated with or endorsed by ppy (osu!) or Team Shanghai Alice (Touhou Project).

- The Touhou Project characters belong to ZUN / Team Shanghai Alice.
- The character art, sounds and other assets in `Resources` belong to their original creators.
- osu! belongs to ppy.
- The code was written by Claude Opus 5.5 (Anthropic) from the author's prompts.

No license is given, and the author claims no ownership of this work. If you own something used here and want it credited or removed, please open an issue.
