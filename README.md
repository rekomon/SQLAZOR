# SQLAZOR

A small Blazor Server app that reverse-engineers a SQL Server database into
clean, hand-written-looking EF Core code:

- One **POCO** class per table (`Entities/`)
- One **`IEntityTypeConfiguration<T>`** per table, using Fluent API only —
  no data annotations — with real table/column names preserved via
  `ToTable(...)` / `HasColumnName(...)` (`Configurations/`)
- One **`DbContext`** wiring it all together via `ApplyConfigurationsFromAssembly`

It talks to SQL Server directly through `sys.tables`, `sys.columns`,
`sys.foreign_keys`, and `sys.indexes` — the same catalogs EF's own scaffolder
uses — so it picks up real column types, nullability, identity columns,
computed columns, composite primary keys, unique indexes, and FK delete
behavior.

## Running it

```bash
cd SQLAZOR.Portal
dotnet run
```

Then open the URL shown in the console (defaults to `http://localhost:5236`).

Requires the ASP.NET Core SDK (net8.0) and a NuGet feed reachable for
`Microsoft.Data.SqlClient` (the only external package this project needs).

[![Youtube Video]()](https://youtu.be/AosK7H4_hTY)


## Using it

1. **Connect** — paste a connection string, e.g.
   `Server=localhost;Database=MyDb;User Id=sa;Password=...;TrustServerCertificate=True;`
   Click **Test Connection**, then **Load Schema**.
2. **Choose tables** — everything is pre-selected except views (views are
   listed if you tick "Include views", but they won't get FK navigation
   properties since views rarely carry real constraints).
3. **Generate** — set your root namespace and `DbContext` class name, click
   **Generate**.
4. **Preview / Download** — click through files in the left-hand list to
   preview them, or grab everything at once as a `.zip`.

## Stored procedures

Procedures don't map cleanly onto EF entities, so they get a different treatment
than tables: no `DbContext`, no Fluent config. Instead, for each procedure you
select, SQLAZOR:

1. Reads its parameters from `sys.parameters`.
2. Asks SQL Server to describe its first result set via
   `sys.dm_exec_describe_first_result_set` — this is metadata inference, not
   real execution, so it's safe to run against any procedure without side effects.
3. If the result set **can** be described: generates a `{Name}Result` POCO plus
   a static `{Name}Executor` class with a hand-written `ExecuteAsync(SqlConnection, ...)`
   method — explicit `SqlParameter`s (with correct `SqlDbType`, size, and
   precision/scale), a manual `SqlDataReader` → POCO mapper using
   `GetOrdinal("column")` + typed `GetXxx()` calls with null checks, no reflection.
4. If it **can't** be described (dynamic SQL, temp-table dependencies, etc.):
   still generates the executor with full parameter binding, but skips the
   result POCO/mapper and leaves a comment explaining why, plus the actual
   SQL Server error message for that procedure.
5. If the procedure has output parameters, an `{Name}Output` POCO is added and
   the executor reads parameter `.Value` back after the reader closes; if it
   *also* has a result set, both are wrapped in a `{Name}ExecutionResult`.

These land in `Entities/` and `Data/` alongside the table-based output, and
ship in the same download zip.

## AI schema assistant (Ollama)

Section 4 of the page is a small chat panel wired to your own [Ollama](https://ollama.com)
instance — nothing is sent anywhere else. Point it at your endpoint (default
`http://localhost:11434`) and model name, hit **Test connection**, then ask
things like:

- "which tables reference Customers?"
- "what's this table probably used for?"
- "write a LINQ query for the top 10 customers by total order value"
- Arabic works too — the assistant is told to reply in whatever language you write in.

How it works: every message is sent to `POST {endpoint}/api/chat` with a
system prompt built from `SchemaContextBuilder` — a compact text rendering of
every table, its columns (name, SQL type, nullability), PK/FK markers, and
the stored procedure list. The model never touches your actual data, only
the schema shape. Conversation history is per-session (`GenerationState`) and
clears if you reload the page or click **Clear**.

If Ollama isn't reachable, isn't running, or the model name is wrong, the
error surfaces inline in the chat rather than failing silently.

### Running the queries it writes

Any `sql`-tagged code block the assistant produces gets a **▶ Run query** button under it.
Clicking it executes the query against your connected database and shows the results as a table
inline in the chat. Deliberately restrictive for safety:

- Only statements starting with `SELECT` or `WITH` are allowed — anything containing
  `INSERT`/`UPDATE`/`DELETE`/`DROP`/`ALTER`/`EXEC`/`MERGE`/`CREATE`/etc. anywhere in the text is
  rejected outright with an inline error. No confirmation dialog to click through, no override —
  if it's not a read query, this feature simply won't run it.
- Capped at 200 rows and a 15-second command timeout, enforced server-side regardless of what the
  query itself asks for.
- Nothing runs automatically — you always click the button yourself, and each block's result
  (or error) stays attached to that message.

The system prompt asks the model to tag SQL blocks with `sql` specifically so the button shows up
reliably; a block with no language tag that still starts with `SELECT`/`WITH` gets the button too,
as a fallback.

## AI naming & documentation (Ollama)

Section 5. For each selected table, sends only its column list (names + SQL
types — never row data) to Ollama and asks it to:

- Suggest a clearer C# class/property name **only** where the mechanical
  PascalCase result is genuinely unclear (cryptic abbreviations, unclear
  acronyms) — already-clear names are left untouched by design, so it doesn't
  needlessly rename things.
- Write a short one-line XML doc `<summary>` for the class and for any
  property whose purpose isn't obvious from its name.

This uses Ollama's structured JSON output mode (`"format": "json"` on
`/api/generate`), parsed defensively — a malformed reply for one table just
skips that table rather than aborting the batch.

Nothing is applied automatically. Tick the checkbox next to a table to apply
its suggestion; this mutates the in-memory `TableInfo`/`ColumnInfo` directly
(the same objects `Generate()` reads from), so it takes effect immediately —
and un-ticking cleanly reverts to the mechanical name, recomputed fresh
rather than cached. Applied summaries show up as real `<summary>` comments in
the generated POCOs.

## Implicit relationship detection (heuristic, no AI call)

Section 6. Flags columns that *look* like foreign keys by naming convention
(`CustomerId`, `Customer_Id`, …) and type-compatibility with a matching
table's primary key, but have no real FK constraint in the database —
common in older or organically-grown schemas. This is deliberately **not**
an LLM call: naming + type matching is a solved, deterministic problem, and
guessing table relationships is exactly the kind of thing a language model
can confidently get wrong without ever touching the actual data.

Detected candidates are shown with a confidence level (`High` = column base
name matches the target table's class name exactly; `Medium` = a looser
match) and a plain-language reason. Nothing is added automatically — tick
the ones you know are real. Accepted candidates are merged into a synthetic
`ForeignKeyInfo` (constraint name suffixed `_Inferred`) purely for the
generation pass; they're never written back to the discovered schema, so
unticking one cleanly removes it. Once accepted, they flow through the exact
same navigation-property and Fluent API code path as a real FK — same
disambiguation logic, same `HasOne().WithMany()` wiring.

## Blazor Server UI pages (list + create/edit, plain HTML or MudBlazor)

Once "Also generate CRUD DTOs + services" is checked, a fourth checkbox — "Also generate Blazor
Server UI pages" — becomes available, with a MudBlazor sub-option underneath it. Per table (with
a primary key), it adds a dedicated folder:

```
Pages/
  Patient/
    PatientList.razor    -- @page "/patients"
    PatientCreate.razor  -- @page "/patients/create"
    PatientEdit.razor    -- @page "/patients/edit/{Id:int}"  (or /{OrderId}/{LineNumber} etc. for a composite key)
```

- **`{Table}List.razor`** — grid of every row (`GetAllAsync()`), an Edit link per row, and a
  Delete button that calls `DeleteAsync(...)` and reloads on success. Loading/error states are
  driven directly off `ResponseResult.IsSuccessful`/`.Message`.
- **`{Table}Create.razor`** — a blank `{Table}CreateDto`, submitted via `CreateAsync`.
- **`{Table}Edit.razor`** — route parameters typed to match the PK (with a Blazor route
  constraint like `:int`/`:guid` applied automatically where the type supports one), fetches the
  row via `GetByIdAsync` on load and copies it field-by-field into a `{Table}UpdateDto`, submitted
  via `UpdateAsync`.

All three `@inject I{Table}Service` directly — not the concrete `{Table}Service` or
`{Table}HttpService` class — so they compile and work unmodified against **whichever
implementation is registered in DI**, Dapper or HttpClient, with zero changes either way.

### Plain HTML mode (default)

Plain HTML `<input>` elements with `@bind-value`, typed per column
(`text`/`number`/`checkbox`/`datetime-local`), not the `InputText`/`InputNumber` component family —
simpler to generate correctly across all the SQL type variations. `byte[]` (binary/rowversion) and
`TimeSpan` columns aren't auto-renderable (not natively supported by Blazor's `@bind-value`
conversion) — those are skipped with a comment rather than emitting something that won't compile.

### MudBlazor mode

Tick "Use MudBlazor components" to swap the markup for `MudTable`/`MudTextField`/`MudNumericField`/
`MudCheckBox`/`MudForm`/`MudButton`/`MudAlert`/`MudProgressCircular` throughout. Same skip rule for
non-renderable types (`byte[]`, `TimeSpan`, `object`); `DateTime`/`DateTimeOffset`/`Guid` use
`MudTextField<T>` (its default `ToString()`/`Parse` converter) rather than `MudDatePicker`, since
that avoids the separate nullable-`Date` binding `MudDatePicker` requires.

**This mode needs the target project to already have MudBlazor set up** — the generator doesn't
touch `Program.cs` or `_Imports.razor` for you:

```bash
dotnet add package MudBlazor
```

```csharp
// Program.cs
builder.Services.AddMudServices();
```

```razor
@* In your root layout (MainLayout.razor or similar) *@
<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />
```

```html
<!-- In App.razor / _Host.cshtml <head> -->
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

This is deliberately minimal, unstyled-beyond-the-basics scaffolding either way — a starting point
to wire up, not a finished admin UI. No client-side validation beyond whatever you add yourself, no
delete confirmation dialog, no pagination on the grid.

## Page style: Plain / MudBlazor / Tabler

Once Blazor pages are enabled, pick one of three markup families for the List/Create/Edit pages
*and* the admin shell (`MainLayout`/`NavMenu`/`Dashboard`) generated by the project scaffold:

- **Plain** — no extra package. Bootstrap-esque classes (`form-control`, `mb-3`, `btn btn-primary`),
  styled by the generated `wwwroot/css/admin.css` (dark sidebar, light content, ~200 lines).
- **MudBlazor** — `MudTable`, `MudTextField`/`MudNumericField`/`MudCheckBox`, `MudForm`,
  `MudLayout`/`MudAppBar`/`MudDrawer`/`MudNavMenu`. Needs the setup described further down.
- **Tabler** — the [preview.tabler.io](https://preview.tabler.io) admin dashboard look: its
  standard `navbar-vertical` sidebar shell, `page-header`/`page-title` per page, `card`-wrapped
  tables (`table-vcenter card-table`) and forms, all loaded from Tabler's CDN
  (`@tabler/core@1.0.0-beta20`) — **no NuGet package needed**, just the CSS/JS `<link>`/`<script>`
  tags in `Components/App.razor`. Since Tabler is Bootstrap 5 under the hood, its form/table
  classes are close enough to the Plain style's that both share the same field-rendering code —
  only the surrounding page chrome (header, card wrapping, sidebar) actually differs.

## AI dashboard insights (charts + stats from Ollama)

Once "Also generate a full runnable project" **and** CRUD services are both checked, a new
sub-section appears: **"Suggest dashboard insights (AI)"**. Clicking it:

1. Sends Ollama a prompt scoped to just the selected tables (and the FKs between them), asking for
   up to 4 chart-worthy queries — counts by category, trends over time, that kind of thing.
2. **Test-runs every suggestion for real** against your connected database via the same read-only
   query safety check the AI chat's "Run query" button uses (`SELECT`/`WITH` only, no write
   keywords, 20-row cap for this use). A suggestion that fails to run, or doesn't resolve to
   exactly two columns (label, value), is marked invalid with the actual error shown — never
   silently dropped.
3. Validated suggestions are **pre-selected** and shown with a live preview of the first few
   (label, value) pairs so you can sanity-check them before generating anything.

Only what you leave checked gets baked in. Accepted insights produce:

- **`Services/DashboardStatsService.cs`** — one `Task<int> Get{Table}CountAsync()` per table with
  CRUD services (row counts, always included whenever CRUD services exist, no AI needed for those),
  plus one `Task<List<ChartDataPoint>> Get{Title}Async()` per accepted insight, running the
  validated SQL via Dapper and shaping the result into `(Label, Value)` points regardless of what
  the AI actually named its result columns.
- **Dashboard stat cards** — one per table, showing the live row count.
- **Dashboard charts** — one per accepted insight, rendered per page style:
  - **MudBlazor**: native `<MudChart Type="ChartType.Bar/Pie/Line">` — no JS involved.
  - **Tabler / Plain**: a `<div id="chart-N">` placeholder filled via a small JS interop call
    (`wwwroot/js/charts.js`, generated only when needed) wrapping
    [ApexCharts](https://apexcharts.com) (loaded from CDN, only when there's at least one chart).

## Full project scaffold (Program.cs, .csproj, admin dashboard shell)

Once CRUD services are enabled, "Also generate a full runnable project" ties everything else into
an actual project you can `dotnet run` straight after unzipping — plus an **Application name**
field (used for the `.csproj` filename, `<AssemblyName>`, and page titles/brand text).

Generated files:

- **`{App}.csproj`** — `net8.0`, `Microsoft.EntityFrameworkCore.SqlServer` + `.Design` always;
  `Microsoft.Data.SqlClient` + `Dapper` + `Mapster` only if CRUD services were generated;
  `MudBlazor` only if MudBlazor pages were generated. No package you didn't ask for.
- **`Program.cs`** — `AddRazorComponents().AddInteractiveServerComponents()` always; conditionally
  adds `AddControllers()`/`MapControllers()` (endpoints), `AddDbContext<{DbContext}>(...)` (always,
  using the connection string below), a scoped `IDbConnection` + `AddGeneratedCrudServices()`
  (CRUD services), and `AddMudServices()` (MudBlazor) — each block only appears if you actually
  generated the thing it wires up.
- **`appsettings.json`** — `ConnectionStrings:DefaultConnection` pre-filled with the connection
  string you used earlier in SQLAZOR, so the project runs immediately. **Move this to user
  secrets or an environment variable before this leaves your machine** — a comment in the file
  says so too.
- **`Properties/launchSettings.json`**, **`Components/App.razor`**, **`Components/Routes.razor`**,
  **`Components/_Imports.razor`** — standard .NET 8 Blazor Server shell. The imports file pulls in
  `{rootNamespace}.Entities/.Dtos/.Services/.Common` (and `MudBlazor` if applicable) project-wide —
  **this is what makes the individually-generated table pages compile at all**, since those pages
  reference `I{Table}Service`/`{Table}Dto` without their own `@using` lines. If you generate Blazor
  pages *without* also generating this scaffold, add those four `@using` lines to your own
  `_Imports.razor` yourself, or the pages won't compile.
- **`Components/Layout/MainLayout.razor`** + **`NavMenu.razor`** — a genuine admin-dashboard shell:
  sidebar with a link per generated table, content area. Three flavors depending on the page style
  (see below): MudBlazor's `MudLayout`/`MudAppBar`/`MudDrawer`/`MudNavMenu`, Tabler's standard
  `navbar-vertical` shell, or a hand-written dark-sidebar-on-light-content layout for Plain
  (`wwwroot/css/admin.css`, ~200 lines — also styles the Plain-mode list/form pages' tables,
  buttons, and alerts).
- **`Components/Pages/Dashboard.razor`** — lands at `/`. A stat card per table with CRUD services
  (live row count), a chart per accepted AI dashboard insight (see below), and a card per table
  with Blazor pages linking to its list page.

## CRUD DTOs + services (Dapper + Mapster, `ResponseResult<T>`, optional API layer)

Tick "Also generate CRUD DTOs + services" before generating and, per selected
table (skipping any without a primary key), you get:

- `Common/ResponseResult.cs` — generated once, shared by every service and controller:
  ```csharp
  public class ResponseResult<T>
  {
      public bool IsSuccessful { get; set; } = false;
      public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.Created;
      public string Message { get; set; } = string.Empty;
      public T Data { get; set; } = default!;
      public int? TotalCount { get; set; } = 0;
  }
  ```
- `Dtos/{Table}Dto.cs` / `{Table}CreateDto.cs` / `{Table}UpdateDto.cs` — same shapes as
  before (Create excludes identity/computed columns, Update excludes the PK and computed columns).
- `Services/I{Table}Service.cs` + `Services/{Table}Service.cs` — **Dapper-backed**, not EF.
  Each method (`GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`) has
  hand-written SQL (`SELECT * FROM ...`, `INSERT ... OUTPUT INSERTED.* VALUES ...`,
  `UPDATE ... SET ... WHERE ...`, `DELETE ... WHERE ...`), executed via `IDbConnection` +
  `Dapper.CommandDefinition` (so cancellation tokens flow through properly), wrapped in a
  try/catch that turns any exception into a `ResponseResult` with `StatusCode = InternalServerError`
  rather than throwing. Entity↔DTO conversion uses **Mapster**'s `Adapt<T>()` — convention-based
  on matching property names (which is guaranteed here since the DTOs were generated from the
  same column list as the entity), so there's no hand-written mapper method to keep in sync.
  A missing row on `GetByIdAsync`/`UpdateAsync`/`DeleteAsync` returns `StatusCode = NotFound`
  with `IsSuccessful = false`, not an exception.
- Composite primary keys work the same way as before — multiple method/route parameters in PK
  ordinal order — but now also drive the generated SQL's `WHERE` clause and Dapper parameter
  objects directly, from one shared helper, so the naming can't drift between the two.

**Also generate API endpoints** adds, per table, `Controllers/{Table}sController.cs` — a plain
`[ApiController]` with `GET` / `GET {id}` / `POST` / `PUT {id}` / `DELETE {id}`, each just
awaiting the service call and returning `StatusCode((int)result.StatusCode, result)` — the
`ResponseResult<T>`'s own status code drives the actual HTTP response, so the controller carries
no branching logic of its own.

**Also generate HttpClient service classes** (only available once endpoints are also checked)
adds `Services/{Table}HttpService.cs` per table — implementing the **same** `I{Table}Service`
interface as the Dapper version, but making HTTP calls (`GetAsync`/`PostAsJsonAsync`/etc.) to the
generated controller instead. Both share `Services/ApiHttpServiceBase.cs`, which turns any
`HttpResponseMessage` into a `ResponseResult<T>` (falling back to a synthesized error result if
the body isn't valid JSON). Because both implementations share one interface, a consuming project
can register whichever one matches its architecture — same call sites either way.

`Services/GeneratedServiceCollectionExtensions.cs` exposes `AddGeneratedCrudServices()` (the
Dapper-backed registrations) and, when HttpClient services were generated,
`AddGeneratedCrudHttpServices(configureClient)` (the HTTP-backed ones) — register **one or the
other** per interface, not both, since they'd collide on the same `I{Table}Service` registration:
```csharp
// This project owns the database:
builder.Services.AddScoped<IDbConnection>(_ => new SqlConnection(connectionString));
builder.Services.AddGeneratedCrudServices();

// OR this project calls the API remotely:
builder.Services.AddGeneratedCrudHttpServices(client => client.BaseAddress = new Uri("https://your-api/"));
```

**Target-project packages needed:** `Dapper`, `Mapster` (for the services), and the ASP.NET Core
Web API metapackage (for the controllers, if generated) — none of these are required for the base
table/`DbContext` output, only for this CRUD layer.

## What the generator does with relationships

- Every FK becomes a reference navigation property on the child (FK-holding)
  entity and a matching `ICollection<T>` on the parent, wired up with
  `HasOne(...).WithMany(...).HasForeignKey(...)` and the FK's real constraint
  name via `HasConstraintName(...)`.
- `OnDelete(...)` mirrors the database's actual delete action
  (`CASCADE` → `DeleteBehavior.Cascade`, etc.) instead of defaulting to EF's
  usual `ClientSetNull` guess.
- If a table has two FKs to the same referenced table (e.g. `Orders` with
  both `BilledToCustomerId` and `ShippedToCustomerId` pointing at
  `Customers`), navigation property names are disambiguated automatically
  instead of colliding.
- Composite primary keys, non-PK unique indexes, `nvarchar(max)` /
  `varchar(max)`, `decimal(p,s)` precision, and identity/computed columns are
  all read and reflected in the Fluent config.


## Notes

- Only the schemas/tables you select are used for relationship resolution —
  if a table's FK points at a table you didn't select, that particular
  navigation property is simply omitted (its scalar FK column still
  generates normally).
- This was written and reviewed by hand in this environment; it has **not**
  been compiled/run here (no outbound NuGet access in this sandbox), so treat
  it as a solid first pass — do a `dotnet build` locally and skim the output
  for your actual schema's edge cases (unusual types, multi-schema FKs,
  extremely long identifiers, etc.) before trusting it wholesale.
