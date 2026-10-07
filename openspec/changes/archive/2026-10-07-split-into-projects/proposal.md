# Proposal

## Why

All backend code lives in the one web project, so nothing enforces where one part ends and the next begins:
- the controllers can reach the database directly
- the receipt rules sit next to the model client
- everything is `public`

The app is about to grow, starting with user accounts. New features are easier to keep in their place if
projects enforce the boundaries before they arrive.

## What Changes

- **The SDK is pinned.** `global.json` pins the .NET 10 SDK, and CI installs the SDK it names. The local tool
  manifest moves to `.config/dotnet-tools.json`, where `dotnet new tool-manifest` puts it.
- **The solution moves to `.slnx`.** Its solution folders match the folders on disk (`src`, `tests`,
  `.github/workflows`), Solution Items lists the build and documentation files, and it keeps only the `Any CPU`
  platform.
- **Shared build settings and central package management.** `Directory.Build.props` and `Directory.Packages.props`
  hold the shared settings and every package version, and `tests/Directory.Build.props` holds the test setup.
  Package versions stay exactly as they are.
- **Warnings are errors in CI.** The build is cleaned up first so that it passes.
- **Projects under `src/`, tests under `tests/`.** The common .NET layout: the host moves to `src/ReceiptSplit`,
  and the tests to `tests/`.
- **Libraries.** The backend splits into a thin host and libraries in `src/`, with the dependency chain
  host → Receipts → Data and Extraction:
  - **`ReceiptSplit`** (the host): only `Program.cs`, the controllers and their DTOs.
  - **`ReceiptSplit.Receipts`**: the business logic.
  - **`ReceiptSplit.Data`**: everything to do with the database.
  - **`ReceiptSplit.Extraction`**: reading a photo with the model.
- **One test project per library.** The tests split into one project per library, plus the API tests through the
  host. A support library, `ReceiptSplit.Testing`, holds the helpers they share.
- **Each library registers its own services**, through an `Add…` extension on `IHostApplicationBuilder`, so
  `Program.cs` never registers library types.
- **Types are internal by default.** A library exposes its internals to its own test project only.
- **The Dockerfile moves to the repository root.** It copies the project files with `COPY --parents` before
  restoring, so the NuGet and npm restore stay cached and a new library needs no Dockerfile change.
  `.dockerignore` keeps the docs, specs and agent files out of the build context, so editing them doesn't rebuild
  the image. Visual Studio container leftovers are removed from the host's project file.
- **Commands and paths are updated** in CI, `.dockerignore`, AGENTS.md, the README and the test helpers.

There is no behavior change: same API, same database, same image contents.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None. This is a restructuring with no change to what the app does, so the change sets `skip_specs: true`.

## Impact

- **Code:** every backend file moves, and most namespaces change with it (`ReceiptSplit.Extraction.ReceiptService`
  becomes `ReceiptSplit.Receipts.ReceiptService`). Migrations move with `AppDbContext` into `ReceiptSplit.Data`.
  EF records applied migrations by id, so existing databases are unaffected.
- **Commands:**
  - `dotnet run --project src/ReceiptSplit` starts the backend.
  - `dotnet test` runs across the solution.
  - Test filters name a test project.
  - `dotnet ef` needs `--project src/ReceiptSplit.Data --startup-project src/ReceiptSplit`.
- **IDE:** run configurations that name `ReceiptSplit/ReceiptSplit.csproj` need the new path. Development still
  keeps its database in `data/` at the repository root.
- **CI:** the SDK from `global.json`, the build and test commands, warnings as errors, and the Dockerfile path.
- **Image:** the same contents, built from a root `Dockerfile`.
