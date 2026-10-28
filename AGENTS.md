# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A dig-and-dodge puzzle game in the style of the 1984 Commodore 64 classic Boulder Dash, in C# on .NET 10 and
Avalonia 12. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). Everything in it
is original: the eight caves and the graphics are made in code, and the sounds and tunes are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVABoulderDash.slnx                  # the SDK version is in global.json (10.0.400 or later)
dotnet test AVABoulderDash.slnx                   # every test runs headless: no window opens
dotnet run --project src/AVABoulderDash           # play it; --no-audio for silence
BOULDER_SHOTS=$PWD/Images dotnet test tests/AVABoulderDash.Tests --filter ScreenshotTests   # refresh the README's pictures
BOULDER_PROBE=1 dotnet test tests/AVABoulderDash.Tests --filter PilotProbe   # how the computer player does in each cave ("lazy" for no input)
python3 tools/make_sounds.py                      # rebuild src/AVABoulderDash/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVABoulderDash/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and how a cave is going.
  - `GameEngine.Play.cs` moves the cave in turns of 1/8 s: Digby, boulders and diamonds falling, fireflies,
    butterflies, slime and the magic wall.
  - `Cave.cs` has `Controls`, the screen measures (`Field`), the tiles and the cave grid; `CaveBook.cs` holds
    the eight caves, A to H, each scattered from its own seed and then drawn over.
  - `Pilot.cs` is the computer player, used by the demonstration and the tests. It tries its next steps out on a
    copy of the cave before taking them.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVABoulderDash/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame, a window onto
  the cave that follows Digby, and runs the 60 Hz timer.
- `src/AVABoulderDash/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVABoulderDash.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `CaveTests` has the computer player clear
  every cave without dying; `PilotProbe` does nothing unless `BOULDER_PROBE` is set.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances in fixed 1/60 s steps (`Field.Step`), and the cave itself moves
  in whole turns, independent of the frame rate. The caves and the engine's `_rng` are seeded, so a game plays
  the same every time. Never use `Random.Shared` in the engine.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Up/down on keys and stick must produce the same `dy` sign the engine
  expects (negative is up the screen). Test new controls through `InputHandler`, not by setting `Controls`
  directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVABoulderDash`); tests
  load the table from a temporary folder and run the engine with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change a cave, the computer player must still clear it in `CaveTests`.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
