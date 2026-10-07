# Tasks

Each numbered group is one commit on the branch `split-into-projects`. Every commit builds, and every existing
test still passes: the same number of tests, passed and skipped. Files move with `git mv`, and the moving commit
only touches namespaces and `using` lines. Each group updates the parts of AGENTS.md, CI and the README it makes
outdated.

## 1. SDK and tool manifest

- [x] 1.1 Record the baseline with `dotnet build ReceiptSplit.sln` and `dotnet test --project ReceiptSplit.Tests`:
  the test counts (passed, skipped) and the build warnings.
  Baseline: 152 tests, 149 passed and 3 skipped (the two live tests and the sample outputs test); 0 warnings in
  Debug and Release.
- [x] 1.2 Add `"sdk": { "version": "10.0.100", "rollForward": "latestFeature" }` to `global.json`, and point CI's
  `setup-dotnet` at it with `global-json-file` instead of `dotnet-version`. Verify that `dotnet --version` in the
  repository picks the installed 10.0 SDK and that the build and tests match the baseline.
  `dotnet --version` gives 10.0.112, the SDK installed here.
- [x] 1.3 `git mv dotnet-tools.json .config/dotnet-tools.json`. Verify that `dotnet tool restore` and `dotnet ef
  --version` still work from the root and from the host's folder.

## 2. Solution, shared settings and central package management

- [x] 2.1 Migrate to `ReceiptSplit.slnx` (`dotnet sln migrate`), delete `ReceiptSplit.sln`, and add the
  `/Solution Items/` and `/.github/workflows/` folders as design.md lists them. Keep only the `Any CPU` platform.
  Update `TestPaths` to look for `ReceiptSplit.slnx`, and the solution name in CI and AGENTS.md. Verify that
  `dotnet build` and `dotnet test` at the root give the baseline, and that the sample-based tests still find
  `samples/` when present.
  The client project gets `<Build Project="false" />`, as the old solution didn't build it either.
- [x] 2.2 Add the root `Directory.Build.props`:
  - conditioned on `.csproj`
  - `net10.0`, `Nullable`, `ImplicitUsings`
  - warnings as errors when `CI` is `true`
  - `InternalsVisibleTo` for `$(MSBuildProjectName).Tests` on projects not ending in `.Tests`

  Add `Directory.Packages.props` with every current package at its current version, and remove those settings and
  versions from both `.csproj` files. Verify:
  - the build and tests match the baseline
  - `dotnet list package` shows the same versions as before
  - the client project still restores, which runs `npm install`
- [x] 2.3 Build with `CI=true dotnet build` (warnings as errors), and fix every warning that turns up without
  disabling the analyzers that raised it. Note in CI's build step where the setting comes from. Verify that
  `CI=true dotnet build` and `CI=true dotnet test` pass.
  No warnings turned up, in Debug or Release, so nothing needed fixing.

## 3. src/ and tests/

- [x] 3.1 `git mv ReceiptSplit src/ReceiptSplit` and `git mv ReceiptSplit.Tests tests/ReceiptSplit.Tests`, and
  update the solution. Update the paths relative to the host: `SpaRoot` and the client reference, Development's
  `Storage:Root` (`../../data`) and the sample path in `ReceiptSplit.http`. Verify that the build and tests match
  the baseline, and that a Development backend run from `src/ReceiptSplit` keeps its database in `data/` at the
  root.
  A Development backend run from `src/ReceiptSplit`, in a worktree with a copy of `data/`, applied no migration
  and listed the copy's 28 receipts, and created nothing under `src/`. The generated `spa.proxy.json` points at
  `receiptsplit.client`.
- [x] 3.2 Add `tests/Directory.Build.props`, which imports the root props and sets the test settings and packages
  for `*.Tests` projects, with a note on why the VSTest packages stay. Trim `ReceiptSplit.Tests.csproj` to its
  project reference and `Microsoft.AspNetCore.Mvc.Testing`. Verify that the tests match the baseline.
- [x] 3.3 Update the paths and test commands in CI, the Dockerfile, AGENTS.md, the README and the client's
  comments that name backend files (`dotnet test` for everything, and filtering by test project), and change
  `.dockerignore` from `ReceiptSplit.Tests` to `tests`. Verify that the CI test step's command runs locally with
  the baseline result.

## 4. Shared test support library

- [x] 4.1 Create `tests/ReceiptSplit.Testing`, a plain library. Move `FakeLlamaClient`, `TestImages`,
  `TestPaths` and `SampleTruth` into it and make them `public`; `ReceiptApiFactory` stays in
  `ReceiptSplit.Tests/Support`. It references the host for now and Magick.NET for `TestImages`. Verify that the
  project isn't treated as a test project (no test run for it) and that the tests match the baseline.

## 5. Data library

- [ ] 5.1 Create `src/ReceiptSplit.Data` and move the entities, `ReceiptStatus`, `AppDbContext`, `Migrations/`,
  `DatabaseInitializer`, `Precision` and `StorageOptions` into it. `StorageOptions` changes namespace to
  `ReceiptSplit.Data`; the migrations keep theirs. The host references Data. Verify that the build and tests
  match the baseline.
- [ ] 5.2 Update the `dotnet ef` command in AGENTS.md to `--project src/ReceiptSplit.Data --startup-project
  src/ReceiptSplit`. Verify that `dotnet ef migrations list` with that command lists every migration, and that a
  scratch backend started on a copy of `data/` applies nothing new and lists the existing receipts.

## 6. Extraction library

- [ ] 6.1 Create `src/ReceiptSplit.Extraction` and move `ILlamaClient`, `LlamaClient`, `ExtractionPrompt`,
  `ReceiptOutputParser`, `ReceiptPromotions`, `ModelServerCheck` and `LlmOptions` into it. `LlmOptions` changes
  namespace to `ReceiptSplit.Extraction`. Point `ReceiptSplit.Testing` at Extraction instead of the host, for
  `ILlamaClient`. Verify that the build and tests match the baseline.
- [ ] 6.2 Create `tests/ReceiptSplit.Extraction.Tests`. Move `ReceiptOutputParserTests`,
  `ReceiptPromotionsTests` and `ModelServerCheckTests` into it, except
  `The_app_doesnt_start_when_the_check_fails`, which moves to a `StartupTests` class in `ReceiptSplit.Tests`.
  Verify that the total test counts match the baseline.

## 7. Receipts library

- [ ] 7.1 Create `src/ReceiptSplit.Receipts` and move the rest of `Extraction/` into it, with namespace
  `ReceiptSplit.Receipts`: `ReceiptService`, `ReceiptExtractor`, `ExtractionWorker`, `ExtractionQueue`, the
  checks, discount, tax codes, edit, results, `ImagePreparer` and `ExtractionErrors`. Receipts references Data and
  Extraction, and the host references Receipts. Remove the host's empty `Data/`, `Extraction/` and `Options/`
  folders. Verify that the build and tests match the baseline.
- [ ] 7.2 Create `tests/ReceiptSplit.Receipts.Tests` and move `ReceiptChecksTests`, `ReceiptTaxCodesTests`,
  `ExtractionErrorsTests` and `ImagePreparerTests` into it. Verify that the total test counts match the baseline.
- [ ] 7.3 Check the dependency direction. Data and Extraction don't reference each other or anything above them,
  and the host's `.csproj` has no package reference that only a library needs. Verify by reading the project
  references and with `dotnet list package` per project.

## 8. Service registration extensions

- [ ] 8.1 Add `AddDatabase()` to Data, `AddExtraction()` and `IServiceProvider.CheckModelServerAsync()` to
  Extraction, and `AddReceipts()` to Receipts, all on `IHostApplicationBuilder` as design.md describes. Each one
  does nothing when called again, and `AddReceipts()` calls the other two first. `Program.cs` calls the three
  methods and `CheckModelServerAsync()` in place of its own registrations. Verify:
  - `CI=true dotnet build` passes
  - the tests match the baseline, including the startup check test and the API tests that swap in the fake model
    client
  - a scratch backend over a copy of `data/` starts, applies no migration and lists the receipts
- [ ] 8.2 In AGENTS.md, describe the registration convention: each library has an `Add…` method on
  `IHostApplicationBuilder` that registers its dependencies first and is safe to call twice, and middleware gets a
  `Use…` method. Update the lines that say `Program.cs` registers services and runs the startup check. Verify by
  reading the section against `Program.cs`.

## 9. Internal by default

- [ ] 9.1 Make every type in the three libraries `internal` unless another production project uses it, by
  narrowing until the build fails and putting back only what's needed. Verify that the build passes, the tests
  match the baseline, and the libraries' public types are only ones the host or another library uses. List them
  in the commit body.

## 10. Dockerfile at the root

- [ ] 10.1 `git mv src/ReceiptSplit/Dockerfile Dockerfile`. Make its restore stage copy `global.json`,
  `Directory.Build.props`, `Directory.Packages.props`, every project file under `src/` (`COPY --parents`) and the
  client's project files before `dotnet restore`. Drop `file:` from CI's build step. Remove
  `DockerDefaultTargetOS` and the `.dockerignore` link from `ReceiptSplit.csproj`. Verify that:
  - `docker build .` succeeds
  - a second build after changing only a `.cs` file reuses the restore layers
  - the image starts and serves `/` and the receipts over a copy of `data/`
- [ ] 10.2 Exclude the Markdown files, `openspec/`, `.claude/` and `.github/` in `.dockerignore`. Verify that a
  build after changing only AGENTS.md or a file in `openspec/` reuses every layer.

## 11. Docs and integration check

- [ ] 11.1 Rewrite the Layout section of AGENTS.md for the new tree: `src/`, `tests/`, the props files, `.config/`
  and the root Dockerfile. Add these conventions:
  - internal by default
  - one test project per library
  - helpers in `ReceiptSplit.Testing`
  - a new library needs a solution entry
  - package versions live in `Directory.Packages.props`

  Check the README for old paths. Verify by reading both against the tree.
- [ ] 11.2 From a clean clone of the branch, run `dotnet build`, `CI=true dotnet test`, `npm run build`,
  `npm run lint` and `docker build .`. Verify that all pass and the test counts match the baseline.
- [ ] 11.3 Run a scratch backend and Vite on spare ports over a copy of `data/`. Verify that receipts list and
  open, and that a photo upload is read to Completed. Then stop the servers and delete the copy.

## Workflow follow-up

- Push and open a PR when the owner asks. After the merge, switch to master, pull, and delete the local branch.
- Archive the change after merging (`/opsx:archive`). It has no specs to merge.
