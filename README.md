# modhash

A small Windows console tool for consent-based Minecraft PC checks (screenshares). It hashes every jar in a mods folder and checks the hashes against Modrinth, so staff only need to review the files that could not be verified.

## Usage

1. Download `modhash.exe` and its `.sha256` file from the [Releases](../../releases) page.
2. Compare the hash: `certutil -hashfile modhash.exe SHA256`
3. Run it from cmd with the mods folder as the argument:

```
modhash "C:\Users\Player\AppData\Roaming\.minecraft\mods"
```

If you run `modhash` without an argument, it asks you to paste the folder path. `modhash --help` shows the usage.

To run it from any folder, put `modhash.exe` in a folder that is on your PATH, or run it with its full path.

In cmd, do not end a quoted path with a backslash (`"C:\mods\"`), because that escapes the closing quote. Leave the last backslash off.

A report is saved next to the exe.

## Run with one command (PowerShell)

`modhash.ps1` downloads the latest `modhash.exe` from the Releases page, checks its SHA-256 against the published hash file, asks for the mods folder (Enter uses the default `.minecraft\mods`), runs it, and deletes the temporary files.

```
powershell -NoProfile -Command "irm 'https://raw.githubusercontent.com/Deflectionn/ModrinthModHashVerifier/main/modhash.ps1' | iex"
```

Notes for staff:

- Do not hide the window. The player should see what is running, and the output has to be visible on the recording.
- This runs code from the internet on the player's PC. It also leaves a fileless-execution pattern in PowerShell and clipboard memory, so do your memory and clipboard checks first, or note in the case file that the hit is yours.
- The hash file comes from the same release as the exe, so it only detects a corrupted download. To verify against a trusted value, compare with the hash published in the README or release notes.

Each file gets one status:

| Status | Meaning |
|---|---|
| VERIFIED | The hash matches a file on Modrinth. The project name and version are shown. |
| UNVERIFIED | The hash was not found on Modrinth. This is not proof of a cheat. |
| NO NETWORK | Modrinth could not be reached, so nothing could be verified. |

The tool also lists archives with a non-jar extension and files named `.jar` that are not archives, and shows the mod ID and version read from `fabric.mod.json`, `quilt.mod.json` and `mods.toml`.

## Important

- UNVERIFIED covers private mods, CurseForge-only mods and modified jars. Review these in a decompiler (Recaf, Luyten) before drawing any conclusion.
- A clean result only means no artifact meeting current criteria was found in the evidence collected.
- Only the top level of the pasted folder is scanned, not subfolders.
- The tool is read-only. It never runs or uploads files, only hashes are sent to Modrinth.
- Use it only for checks the player has agreed to, and follow your server's own procedure.

## Build

Requires the .NET 8 SDK.

```
dotnet publish -c Release
```

The single exe is created in `bin/Release/net8.0/win-x64/publish/`.

## License

MIT
