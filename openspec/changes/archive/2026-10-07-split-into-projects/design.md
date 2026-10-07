# Design

## Context

See proposal.md (Why). Current state:

- **One web project.** `ReceiptSplit.csproj` holds `Controllers/`, `Contracts/` (DTOs), `Data/` (entities,
  `AppDbContext`, migrations, `Precision`, `DatabaseInitializer`), `Extraction/` (the model client, parser, image
  handling, `ReceiptService`, the extractor, worker and queue) and `Options/`. It also references
  `receiptsplit.client.esproj`, so restoring it runs `npm install`.
- **Tests.** One test project at the root, `ReceiptSplit.Tests`, holds unit tests, API tests through
  `ReceiptApiFactory`, opt-in live model tests, and the helpers in `Support/` (`FakeLlamaClient`, `TestImages`,
  `TestPaths`, `SampleTruth`). `TestPaths` finds the repository root by looking for `ReceiptSplit.sln`.
- **Build files.** `ReceiptSplit.sln` lists the two .NET projects and the client project, with `compose.yaml` as
  the only Solution Item, and `x64` and `x86` platforms that nothing builds for. Each `.csproj` repeats `net10.0`,
  `Nullable` and `ImplicitUsings`, and package versions live in each `.csproj`. `global.json` only picks the test
  runner, so the SDK is whatever is installed, and the tool manifest is `dotnet-tools.json` at the root.
- **Docker.** `ReceiptSplit/Dockerfile` copies the host's `.csproj` and the client's project files, restores,
  then copies everything. CI builds it with `context: .` and `file: ReceiptSplit/Dockerfile`.
- **What depends on what.** Measured from the code:
  - The parser, prompt, promotions, model client and `ModelServerCheck` don't use the database.
  - `Precision` is used by `AppDbContext` and by business code, and by nothing that avoids the database.
  - `StorageOptions` is used by `DatabaseInitializer` as well as by business code.

## Goals / Non-Goals

**Goals:**
- Project references enforce the layers: host → Receipts → Data and Extraction.
- Each library's public surface is only what another production project uses.
- One place for shared build settings and one for package versions.
- The folder layout and build files most .NET repositories use, so tools and people find things where they expect.
- An identical app: every existing test passes, with only renamed namespaces and moved files.

**Non-Goals:**
- Changing behavior, the API, the database schema or package versions.
- Making `AppDbContext` modular (separate contexts or per-module database interfaces).
- Changing code to fit the layers better, such as moving the controller's reads of `AppDbContext` into
  `ReceiptService`.
- Restructuring the client.

## Decisions

### SDK and tools
`global.json` gets an `sdk` section next to the test runner:

```json
"sdk": { "version": "10.0.100", "rollForward": "latestFeature" }
```

Any .NET 10 SDK from 10.0.100 on builds the repository, and an installed .NET 11 preview never does. CI's
`setup-dotnet` reads it with `global-json-file` instead of naming `10.0.x` itself, so the version lives in one
place. `.slnx` needs SDK 9.0.200 or later, which this covers.

The tool manifest moves to `.config/dotnet-tools.json`, where `dotnet new tool-manifest` creates it. `dotnet tool
restore` looks there as well as at the root.

*Alternative:* pinning an exact SDK (`latestPatch`). Rejected, because the `sdk:10.0` image, the Ubuntu package
and CI's download move independently, and builds would fail whenever one ran ahead.

### Layout on disk

```
src/
  ReceiptSplit/             the host
  ReceiptSplit.Data/
  ReceiptSplit.Extraction/
  ReceiptSplit.Receipts/
tests/
  Directory.Build.props
  ReceiptSplit.Tests/
  ReceiptSplit.Extraction.Tests/
  ReceiptSplit.Receipts.Tests/
  ReceiptSplit.Testing/
receiptsplit.client/
```

- **`src/` and `tests/`, lowercase**, as in most .NET repositories and the `dotnet new` guidance. The host moves
  too, so every production project sits in one folder.
- **The client stays at the root.** It has its own toolchain and CI job, and moving it would change every client
  path for no gain.
- **Paths relative to the host follow it.** `SpaRoot` and the client reference gain a `..`, Development's
  `Storage:Root` becomes `../../data` so the dev database stays in `data/` at the root, and `ReceiptSplit.http`
  finds `samples/` two levels up.
- **The host moves before the libraries are split out**, in the same commit as the tests, so later commits only
  add projects to `src/` and `tests/`.

### Projects and what goes where

```
ReceiptSplit (host)            Program.cs, Controllers/, Contracts/ (DTOs stay with their controllers)
  |
  v
ReceiptSplit.Receipts          ReceiptService, ReceiptExtractor, ExtractionWorker, ExtractionQueue,
  |          |                 ReceiptChecks, ReceiptDiscount, ReceiptTaxCodes, ReceiptEdit, ReceiptActionResult,
  |          |                 ReceiptImage, InvalidImageException, ReceiptsDeleted, ImagePreparer, ExtractionErrors
  v          v
ReceiptSplit.Data              Receipt, ReceiptLine, ReceiptStatus, AppDbContext, Migrations/,
                               DatabaseInitializer, Precision, StorageOptions
ReceiptSplit.Extraction        ILlamaClient, LlamaClient, ExtractionPrompt, ReceiptOutputParser, ReceiptPromotions,
                               ModelServerCheck, LlmOptions
```

- **`ReceiptSplit.Data` owns everything about the database**, entities included. Services use `AppDbContext`
  directly, as they do today.
- **`Precision` lives in Data.** It's a business rounding rule, but every user of it also uses Data. It can move
  to its own project once something that avoids the database needs it.
- **`StorageOptions` lives in Data**, because `DatabaseInitializer` needs it and Data references nothing above it.
- **`ImagePreparer` and `ExtractionErrors` live in Receipts.** Uploads, display copies and the extractor all use
  them. `ImagePreparer` reads `LlmOptions`, which is fine because Receipts references Extraction.
- **Libraries are flat**, apart from `Migrations/`. Namespaces follow the project names. Migrations keep the
  `ReceiptSplit.Data.Migrations` namespace they already have.
- **The Design package stays in the host.** The EF tools build the startup project (the host) to create the
  context, so `Microsoft.EntityFrameworkCore.Design` stays there.

*Alternatives:*
- **A single `ReceiptSplit.Core`:** rejected, because domain-named libraries (`Receipts`, and later `Accounts`)
  keep each one's job clear.
- **Entities in each module with database interfaces (`IReceiptsDb`):** rejected for this app, which has one
  SQLite file and one service. Data owning every table is simpler, and the project boundaries still hold.

### Tests

| Project | Contents | Helpers |
|---|---|---|
| `tests/ReceiptSplit.Extraction.Tests` | `ReceiptOutputParserTests`, `ReceiptPromotionsTests`, `ModelServerCheckTests` without the startup case | `ReceiptSplit.Testing` |
| `tests/ReceiptSplit.Receipts.Tests` | `ReceiptChecksTests`, `ReceiptTaxCodesTests`, `ExtractionErrorsTests`, `ImagePreparerTests` | `ReceiptSplit.Testing` |
| `tests/ReceiptSplit.Tests` | `ReceiptsApiTests`, `LiveExtractionTests`, and `The_app_doesnt_start_when_the_check_fails` moved here | `Support/ReceiptApiFactory`, `ReceiptSplit.Testing` |
| `tests/ReceiptSplit.Testing` | Not a test project. Holds `FakeLlamaClient`, `TestImages`, `TestPaths`, `SampleTruth`, made `public` | |

Test projects never reference each other, and Data gets a test project once it has tests. `TestPaths` looks for
`ReceiptSplit.slnx`.

The test projects keep `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`. `dotnet test` runs on
Microsoft.Testing.Platform (set in `global.json`) and doesn't need them, but tools that still run tests through
VSTest do.

### Solution file
`ReceiptSplit.sln` becomes `ReceiptSplit.slnx` (`dotnet sln migrate`, then edited). It has these solution
folders:
- `/src/`, `/tests/` (holding `tests/Directory.Build.props` too) and `/.github/workflows/` (`ci.yml`), each
  matching its folder on disk
- `/Solution Items/`:
  - `Directory.Build.props`, `Directory.Packages.props`
  - `global.json`, `.config/dotnet-tools.json`
  - `Dockerfile`, `.dockerignore`, `compose.yaml`
  - `README.md`, `AGENTS.md`

The client project sits at the top, as it does on disk. `openspec/` isn't listed, because its files come and go
with every change. The solution keeps only the `Any CPU` platform: every project is `AnyCPU`, and the `x64` and
`x86` entries the old solution carried mapped back to it.

### Shared build settings
- **Root `Directory.Build.props`.** It is conditioned on `'$(MSBuildProjectExtension)' == '.csproj'`, because
  MSBuild would otherwise apply it to the client's `.esproj`. It holds:
  - `TargetFramework` `net10.0`, `Nullable`, `ImplicitUsings`
  - `TreatWarningsAsErrors` when `'$(CI)' == 'true'` (GitHub Actions sets it; the Docker build doesn't)
  - `<InternalsVisibleTo Include="$(MSBuildProjectName).Tests" />` for projects not ending in `.Tests`
- **`tests/Directory.Build.props`.** It imports the root file with `GetPathOfFileAbove`, since MSBuild only picks up
  the nearest `Directory.Build.props`. Then, for projects whose name ends in `.Tests`, it sets `OutputType` `Exe`
  and `IsPackable` `false`, and adds the xUnit v3 packages and `<Using Include="Xunit" />`. `ReceiptSplit.Testing`
  doesn't end in `.Tests`, so it stays a plain library.
- **`Directory.Packages.props`.** `ManagePackageVersionsCentrally`, with every package at the version in use today.
  Project files list packages without versions.

### Each library registers its own services
Each library has an extension method on `IHostApplicationBuilder` that binds its settings section and registers its
services. `Program.cs` calls these methods instead of registering library types itself:

| Method | Library | What it registers |
|---|---|---|
| `AddDatabase()` | Data | `StorageOptions` (resolved against the content root), `AppDbContext`, `DatabaseInitializer` |
| `AddExtraction()` | Extraction | `LlmOptions`, the model client's typed `HttpClient`, `ModelServerCheck` |
| `AddReceipts()` | Receipts | `ImagePreparer`, `ExtractionQueue`, `ReceiptExtractor`, `ReceiptService`, `ExtractionWorker` |

Extraction also offers `IServiceProvider.CheckModelServerAsync()`, which runs the startup check in a scope of
its own.

- **`Add…`, not `Use…`.** ASP.NET reserves `Use…` for request pipeline middleware, and these libraries have
  none.
- **Each library registers what it depends on.** `AddReceipts()` calls `AddDatabase()` and `AddExtraction()`
  first. The library therefore guarantees that `DatabaseInitializer` is registered before `ExtractionWorker`, so
  migrations run before unfinished receipts are re-queued. Before, only a comment in `Program.cs` protected that
  order.
- **Calling a method twice does nothing.** Each one returns early if its services are already registered, because
  more than one library can need the database.
- **`IHostApplicationBuilder`, not `IServiceCollection`.** The libraries need configuration, and Data also needs
  the content root, so binding a library's settings section moves out of `Program.cs`.
- **The extension classes stay in their libraries' namespaces** rather than `Microsoft.Extensions.*`.
- **Before internal by default.** Once the libraries register their own types, the host no longer names
  `DatabaseInitializer`, `LlamaClient`, `ModelServerCheck`, `ExtractionWorker` or `ReceiptExtractor`, so the next
  step can make them internal along with the rest.

### Internal by default
A type is `public` only when another production project uses it. Everything else becomes `internal`. The
`InternalsVisibleTo` above keeps each library's tests working, and `ReceiptSplit.Testing` only needs public types.
The final split comes from making types internal until the build fails, not from a list written in advance.

Some types stay public although only their own library seems to need them:
- **`ExtractionQueue`:** the DI container only uses public constructors, and `ReceiptService`'s public
  constructor takes the queue.
- **`ImagePreparer`:** the opt-in live model tests in `ReceiptSplit.Tests` resolve it from the host's services.
- **`LlmOptions`:** Receipts reads it.

`Program` is generated `public` for top-level statements in .NET 10, so `WebApplicationFactory<Program>` keeps
working.

### Dockerfile at the root
The Dockerfile moves to `./Dockerfile`, since its build context is already the repository root. The restore
stage copies, then restores:
- `global.json`, `Directory.Build.props`, `Directory.Packages.props`
- every project file under `src/`, with `COPY --parents src/*/*.csproj ./`, which keeps each one's folder
- the client's `.esproj`, `package.json` and `package-lock.json`

Code changes then reuse the cached NuGet and npm restore, and a new library is picked up without editing the
Dockerfile. `--parents` is stable from Dockerfile syntax 1.20, so the file starts with
`# syntax=docker/dockerfile:1`, which BuildKit resolves to the newest 1.x frontend. `.dockerignore` excludes
`tests` instead of `ReceiptSplit.Tests`, and also the files the image never needs: the Markdown files,
`openspec/`, `.claude/` and `.github/`. Without them, `COPY . .` copies only what the build reads, so editing a
spec or AGENTS.md no longer rebuilds the image. CI drops `file:`.

`DockerDefaultTargetOS` and the `.dockerignore` link leave the host's `.csproj`. Only Visual Studio's container
tooling uses them, and nothing here does.

*Alternatives:*
- **Restoring after `COPY . .`:** rejected, because every build would rerun `npm install` and the NuGet restore.
- **One `COPY` line per project file:** rejected, because each new library would need a line, and a forgotten one
  only shows up as a failed build.

## Risks / Trade-offs

- **[Risk]** Moved files lose their `git log` history. → Files move with `git mv`. The commit that moves them
  only changes the namespace and `using` lines, plus visibility in the later internal-by-default commit. That
  keeps them similar enough for git's rename detection, so `git log --follow` keeps working. Every commit must
  still build, which rules out a move-only commit.
- **[Risk]** Moving the migrations breaks applying them to existing databases. → EF matches applied migrations by
  id, and the migration ids and namespace stay the same. Verify against a copy of `data/`.
- **[Risk]** Warnings that exist today fail CI. → Build with `-warnaserror` locally and fix them before turning it
  on.
- **[Risk]** IDE run configurations or the owner's local scripts point at old paths. → `launchSettings.json` moves
  with the host and keeps working, and IDEs read the projects from the `.slnx`. Saved run configurations and
  commands like `dotnet run --project ReceiptSplit` need `src/`. Development's `Storage:Root` is updated with the
  move, so the dev database stays where it is.
- **[Trade-off]** The syntax line makes every build pull the `docker/dockerfile:1` frontend first. → It comes
  from Docker Hub, which the base images need anyway, and it keeps `--parents` working on older Docker versions
  too.
- **[Trade-off]** Data is a shared dependency, so modules could touch each other's tables. → Accepted for one
  database and one service. Business rules stay in the module that owns them, by convention.

## Migration Plan

This is a source-only change, with no deployment steps.
