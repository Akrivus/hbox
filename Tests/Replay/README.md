# Saved-episode replay regression harness

Run from the repository root with the .NET 10 SDK:

```sh
dotnet run --project Tests/Replay/ReplayTests.csproj
```

The harness compiles the production FolderSource, FolderConfigs and config
interfaces. Unity lifecycle, Chat loading and playlist boundaries use small
stubs; JSON manifest persistence, file existence, selection, ranking, rotation
history and replay cooldown updates execute the production code.

The fixed clock is October 13, 2026 at noon UTC. Saved JSON fixtures and their
explicit manifest timestamps represent two bulletins today and two yesterday.
Yesterday's highest vote count deliberately exceeds today's: age priority must
win between tiers while votes still rank episodes within a tier. Tests copy the
fixtures into isolated temporary directories and never touch Documents/PBNN.

These fixture episodes have no actors, speech or nodes. They verify saved-file
selection and queue handoff; they are not playable Unity bulletins.
