# ForgeDesk performance notes

## Method

Performance work was limited to a measured persistence bottleneck rather than speculative caching or broad concurrency changes. Measurements used the repository-local .NET SDK `8.0.416`, an isolated temporary SQLite database, and deterministic scan-owned result rows on Debian Linux. Initial fixture seeding was excluded from the timed operation.

Workload per operation:

- 1,500 symbols
- 1,499 dependencies
- 1,500 findings
- Five timed samples for each operation on the old and changed implementation

The result replacement path was measured as a complete delete-and-insert replacement; the clear path measured deletion only. These are engineering measurements for one workload, not user-facing performance guarantees.

## Change and result

`AnalysisRepository` previously materialized all old dependencies, findings, and symbols and passed them through EF's change tracker before deleting them. It now uses EF Core 8 `ExecuteDeleteAsync` in dependency order inside the existing transaction and detaches any tracked scan-owned rows first.

| Operation | Previous samples (ms) | Previous median | Changed samples (ms) | Changed median |
|---|---:|---:|---:|---:|
| Clear | 221, 205, 213, 218, 203 | 213 | 29, 26, 28, 27, 28 | 28 |
| Replace | 977, 878, 404, 402, 347 | 404 | 482, 501, 435, 197, 171 | 435 |

For this temporary workload, clear median time decreased by approximately 87% (185 ms). Complete replacement did not demonstrate a speed gain because inserting the new 4,499-row result set dominates the operation; no replacement improvement is claimed.

## Correctness checks

The optimization is covered by `ForgeDesk.IntegrationTests/AnalysisRepositoryPersistenceTests.cs`, including:

- replacement read-back and old-row tracker detachment;
- dependency/finding foreign-key relationships;
- transaction rollback when replacement rows violate the unique symbol key;
- clear read-back and tracker cleanup.

Run the functional check with:

```bash
make test
```

No timing threshold is part of the normal test suite because host and filesystem variance would make such a test unreliable. Repeat measurements with the same isolated workload before making further performance changes.

## Remaining scaling limits

- Roslyn project loading and semantic-model creation remain the dominant cost for many large solutions.
- Graph snapshot loading currently materializes the bounded scan snapshot before filtering/layout.
- Report queries are bounded and no-tracking, but exporting very large reports still consumes memory proportional to the selected output.
- WPF rendering and accessibility performance remain Windows-only manual checks.
