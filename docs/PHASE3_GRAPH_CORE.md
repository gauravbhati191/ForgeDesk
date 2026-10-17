# Phase 3 graph core

## Scope

The graph core turns the persisted `Symbol` and `Dependency` rows for one selected
`Scan` into a read-only `GraphData` value. It does not add a graph table or persist
layout coordinates. `ForgeDesk.Graph` contains the contracts, filtering, graph
transformation, and deterministic layout; `ForgeDesk.Infrastructure` supplies the
scan-scoped EF Core query; `ForgeDesk.Application` exposes the versioned query
service used by hosts.

```text
IGraphQueryService
  -> IGraphDataProvider
      -> IGraphQueryRepository (AsNoTracking, selected Scan only)
      -> GraphDataBuilder (metadata, policy, filters, selection)
      -> DeterministicGraphLayoutService (in-memory coordinates)
```

## Node and edge semantics

* A node represents one persisted `Symbol`. Its ID is the persisted Symbol GUID,
  not its display name. The node includes symbol kind, symbol key/qualified name
  when it can be derived, source-file relative path, declaration lines, scan ID,
  selection flags, and temporary coordinates.
* An edge represents one persisted `Dependency` whose source and target symbols
  both exist in the selected scan. `RelationshipType` preserves the stored text;
  `Kind` maps known Phase 2 values (`TypeReference`, `Calls`, `Inherits`, and
  `Implements`) and uses `Unknown` for other text.
* Dependencies with a missing endpoint remain counted as persisted unresolved
  rows but are not rendered as connected edges. The result is marked `Partial`.
  No external or fabricated symbol is created.
* A symbol whose source-file metadata is missing is retained with a null source
  location and marks the result `Partial`.

The default malformed-data policies are documented in the contracts:

* exact source/target/relationship duplicates are collapsed;
* self-edges are excluded because Phase 2 intentionally does not emit them;
* callers can explicitly request `Preserve` duplicates or `Include` self-edges.

The result exposes `PersistedNodeCount`, `PersistedEdgeCount`,
`UnresolvedDependencyCount`, `DuplicateEdgeCount`, and `SelfEdgeCount` in addition
to visible and total counts.

## Query behavior

`GraphQueryOptions` combines a scan/project identity, optional selected node, and
`GraphFilterOptions`:

* search is a case-insensitive substring over symbol name, qualified name, symbol
  key, and relative path;
* symbol kinds, relationship text, and relationship kinds can be restricted;
* `Incoming`, `Outgoing`, and `Both` are selection-scoped direction filters;
* a positive neighborhood depth performs a bounded BFS from the selected node;
  zero or an omitted depth leaves the full filtered graph visible;
* `IncludeIsolatedNodes` controls nodes with no visible edge;
* a selected node is marked `IsSelected`, direct neighbors are marked `IsNeighbor`,
  and direct edges are marked `IsHighlighted` with incoming/outgoing flags.

If a filter removes the selected node, the returned selection is cleared. Total
counts remain the persisted scan counts, while `VisibleNodeCount` and
`VisibleEdgeCount` describe the filtered result.

## Data quality states

`GraphData.State` makes incomplete data explicit:

| State | Meaning |
|---|---|
| `Missing` | The scan/project is absent or the requested scan does not belong to the project. |
| `Empty` | No symbols were persisted; `NotStarted` has a distinct status message. |
| `Ready` | Completed analysis counts match the persisted rows and endpoints are usable. |
| `Partial` | Analysis is running, failed, or cancelled, or unresolved dependency rows remain. |
| `Stale` | A completed scan's persisted symbol/dependency row counts no longer match its analysis counts. |
| `Failed` | Reserved read-model state for a host that converts an infrastructure exception into a graph error. |

`GraphQueryService` also returns `GraphQueryResult.IsStaleRequest`. A caller can
give requests monotonically increasing versions; a slower older query is marked
stale instead of being treated as the current selection. Cancellation is passed
through repository queries, graph construction, and layout and is allowed to
surface as `OperationCanceledException`.

## Layout

`DeterministicGraphLayoutService` sorts node IDs ordinally and places them in a
row-major grid. Identical input IDs and layout options therefore receive identical
coordinates, regardless of database query order. Coordinates are temporary
presentation state and are never written to the domain entities.

## Verification

The graph core has unit coverage for metadata projection, stable IDs, filtering,
selection highlighting, duplicate/self-edge policies, deterministic layout, and
cancellation. SQLite integration coverage verifies scan isolation, no-tracking
materialization, real persisted graph loading, and ready/partial/missing states.

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./.dotnet/dotnet restore ForgeDesk.sln --locked-mode
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./.dotnet/dotnet build ForgeDesk.sln --configuration Release --no-restore
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./.dotnet/dotnet test ForgeDesk.UnitTests/ForgeDesk.UnitTests.csproj --configuration Release --no-build --no-restore
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./.dotnet/dotnet test ForgeDesk.IntegrationTests/ForgeDesk.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

The WPF host remains responsible for rendering and source/editor navigation. The
graph core only supplies validated metadata and read-only graph facts.
