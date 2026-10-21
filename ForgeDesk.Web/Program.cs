using System.Text;
using System.Text.Json.Serialization;
using ForgeDesk.Application;
using ForgeDesk.Domain;
using ForgeDesk.Graph;
using ForgeDesk.Infrastructure;
using ForgeDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddForgeDeskInfrastructure(options =>
{
    var databasePath = builder.Configuration["ForgeDesk:DatabasePath"];
    var connectionString = builder.Configuration.GetConnectionString("ForgeDesk");
    if (!string.IsNullOrWhiteSpace(databasePath))
    {
        options.DatabasePath = databasePath;
    }

    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.ConnectionString = connectionString;
    }
});

builder.Services.AddSingleton<AnalysisJobQueue>();
builder.Services.AddSingleton<AnalysisJobStatusStore>();
builder.Services.AddHostedService<AnalysisWorker>();

var app = builder.Build();

await using (var initializationScope = app.Services.CreateAsyncScope())
{
    await initializationScope.ServiceProvider
        .GetRequiredService<DatabaseInitializer>()
        .InitializeAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "ForgeDesk.Web",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/projects", async (ForgeDeskDbContext db, CancellationToken cancellationToken) =>
{
    var projects = await db.Projects
        .AsNoTracking()
        .ToListAsync(cancellationToken);
    projects = projects
        .OrderByDescending(project => project.LastOpenedAtUtc ?? project.UpdatedAtUtc)
        .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var projectIds = projects.Select(project => project.Id).ToArray();
    var scans = projectIds.Length == 0
        ? []
        : await db.Scans
            .AsNoTracking()
            .Where(scan => projectIds.Contains(scan.ProjectId))
            .ToListAsync(cancellationToken);
    scans = scans
        .OrderByDescending(scan => scan.QueuedAtUtc)
        .ToList();
    var latestScans = scans
        .GroupBy(scan => scan.ProjectId)
        .ToDictionary(group => group.Key, group => group.First());

    return Results.Ok(projects.Select(project => WebQueryHelpers.ProjectView(
        project,
        latestScans.GetValueOrDefault(project.Id))).ToArray());
});

app.MapGet("/api/projects/{projectId:guid}", async (
    Guid projectId,
    ForgeDeskDbContext db,
    AnalysisJobStatusStore statusStore,
    CancellationToken cancellationToken) =>
{
    var project = await db.Projects
        .AsNoTracking()
        .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken);
    if (project is null)
    {
        return Results.NotFound(new { error = $"Project '{projectId}' was not found." });
    }

    var scans = await db.Scans
        .AsNoTracking()
        .Where(scan => scan.ProjectId == projectId)
        .ToListAsync(cancellationToken);
    scans = scans.OrderByDescending(scan => scan.QueuedAtUtc).ToList();
    var latestScan = scans.FirstOrDefault();

    return Results.Ok(new
    {
        project = WebQueryHelpers.ProjectView(project, latestScan),
        scans = scans.Select(scan => WebQueryHelpers.ScanView(
            scan,
            statusStore.TryGet(scan.Id, out var job) ? job : null)).ToArray()
    });
});

app.MapPost("/api/projects/onboard", async (
    ProjectOnboardingRequest request,
    IProjectOnboardingService onboarding,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await onboarding.OnboardAsync(request, cancellationToken: cancellationToken);
        return Results.Ok(result);
    }
    catch (ProjectValidationException exception)
    {
        return Results.BadRequest(new { error = exception.Message, errors = exception.Errors });
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/api/projects/{projectId:guid}/scans/{scanId:guid}", async (
    Guid projectId,
    Guid scanId,
    ForgeDeskDbContext db,
    AnalysisJobStatusStore statusStore,
    CancellationToken cancellationToken) =>
{
    var scan = await FindOwnedScanAsync(db, projectId, scanId, cancellationToken);
    return scan is null
        ? Results.NotFound(new { error = "The selected scan was not found for this project." })
        : Results.Ok(WebQueryHelpers.ScanView(
            scan,
            statusStore.TryGet(scan.Id, out var job) ? job : null));
});

app.MapPost("/api/projects/{projectId:guid}/scans/{scanId:guid}/analysis", async (
    Guid projectId,
    Guid scanId,
    ForgeDeskDbContext db,
    AnalysisJobQueue queue,
    AnalysisJobStatusStore statusStore,
    CancellationToken cancellationToken) =>
{
    var scan = await FindOwnedScanAsync(db, projectId, scanId, cancellationToken);
    if (scan is null)
    {
        return Results.NotFound(new { error = "The selected scan was not found for this project." });
    }

    var existingJob = statusStore.TryGet(scanId, out var currentJob) ? currentJob : null;
    if (scan.AnalysisStatus == AnalysisStatus.Running
        || existingJob?.State is "Queued" or "Running")
    {
        return Results.Conflict(new { error = "Analysis is already queued or running for this scan." });
    }

    if (scan.Status != ScanStatus.Completed)
    {
        return Results.BadRequest(new { error = "Only a completed discovery scan can be analyzed." });
    }

    var job = new AnalysisJob(projectId, scanId);
    statusStore.Queue(job);
    await queue.EnqueueAsync(job, cancellationToken);
    return Results.Accepted($"/api/projects/{projectId}/scans/{scanId}/analysis", new
    {
        projectId,
        scanId,
        status = "Queued"
    });
});

app.MapGet("/api/projects/{projectId:guid}/scans/{scanId:guid}/analysis", async (
    Guid projectId,
    Guid scanId,
    ForgeDeskDbContext db,
    AnalysisJobStatusStore statusStore,
    CancellationToken cancellationToken) =>
{
    var scan = await FindOwnedScanAsync(db, projectId, scanId, cancellationToken);
    if (scan is null)
    {
        return Results.NotFound(new { error = "The selected scan was not found for this project." });
    }

    return Results.Ok(WebQueryHelpers.ScanView(
        scan,
        statusStore.TryGet(scan.Id, out var job) ? job : null));
});

app.MapGet("/api/projects/{projectId:guid}/scans/{scanId:guid}/files", async (
    Guid projectId,
    Guid scanId,
    ForgeDeskDbContext db,
    CancellationToken cancellationToken) =>
{
    var scan = await FindOwnedScanAsync(db, projectId, scanId, cancellationToken);
    if (scan is null)
    {
        return Results.NotFound(new { error = "The selected scan was not found for this project." });
    }

    var files = await db.SourceFiles
        .AsNoTracking()
        .Where(file => file.ScanId == scanId)
        .OrderBy(file => file.RelativePath)
        .Select(file => new
        {
            id = file.Id,
            relativePath = file.RelativePath,
            fileName = file.FileName,
            extension = file.Extension,
            language = file.Language,
            sizeBytes = file.FileSizeBytes,
            lastModifiedAtUtc = file.LastModifiedAtUtc,
            discoveryStatus = file.DiscoveryStatus.ToString()
        })
        .Take(10_000)
        .ToListAsync(cancellationToken);

    return Results.Ok(files);
});

app.MapGet("/api/projects/{projectId:guid}/scans/{scanId:guid}/findings", async (
    Guid projectId,
    Guid scanId,
    string? severity,
    string? rule,
    string? path,
    ForgeDeskDbContext db,
    CancellationToken cancellationToken) =>
{
    var scan = await FindOwnedScanAsync(db, projectId, scanId, cancellationToken);
    if (scan is null)
    {
        return Results.NotFound(new { error = "The selected scan was not found for this project." });
    }

    var query = db.Findings
        .AsNoTracking()
        .Where(finding => finding.ScanId == scanId);

    if (!string.IsNullOrWhiteSpace(severity))
    {
        if (!Enum.TryParse<FindingSeverity>(severity.Trim(), true, out var parsedSeverity))
        {
            return Results.BadRequest(new { error = $"Unknown finding severity '{severity}'." });
        }

        query = query.Where(finding => finding.Severity == parsedSeverity);
    }

    if (!string.IsNullOrWhiteSpace(rule))
    {
        var ruleFilter = rule.Trim();
        query = query.Where(finding => finding.RuleId.Contains(ruleFilter));
    }

    if (!string.IsNullOrWhiteSpace(path))
    {
        var pathFilter = path.Trim();
        query = query.Where(finding => finding.SourceFile != null
            && finding.SourceFile.RelativePath.Contains(pathFilter));
    }

    var findings = await query
        .OrderBy(finding => finding.Severity)
        .ThenBy(finding => finding.SourceFile == null ? string.Empty : finding.SourceFile.RelativePath)
        .ThenBy(finding => finding.StartLine)
        .ThenBy(finding => finding.RuleId)
        .Take(10_000)
        .Select(finding => new
        {
            id = finding.Id,
            severity = finding.Severity.ToString(),
            ruleId = finding.RuleId,
            message = finding.Message,
            relativePath = finding.SourceFile == null ? null : finding.SourceFile.RelativePath,
            startLine = finding.StartLine,
            endLine = finding.EndLine,
            symbolName = finding.Symbol == null ? null : finding.Symbol.Name
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(findings);
});

app.MapGet("/api/projects/{projectId:guid}/scans/{scanId:guid}/graph", async (
    Guid projectId,
    Guid scanId,
    string? search,
    string? relationship,
    string? symbolKind,
    string? direction,
    int? depth,
    bool? includeIsolated,
    string? selected,
    ForgeDeskDbContext db,
    IGraphQueryService graphQuery,
    CancellationToken cancellationToken) =>
{
    var scan = await FindOwnedScanAsync(db, projectId, scanId, cancellationToken);
    if (scan is null)
    {
        return Results.NotFound(new { error = "The selected scan was not found for this project." });
    }

    if (!TryParseGraphDirection(direction, out var graphDirection, out var directionError))
    {
        return Results.BadRequest(new { error = directionError });
    }

    if (!TryParseEnumList(symbolKind, out IReadOnlyCollection<SymbolKind>? symbolKinds, out var symbolError))
    {
        return Results.BadRequest(new { error = symbolError });
    }

    var relationshipTypes = SplitFilter(relationship);
    var filters = new GraphFilterOptions(
        search,
        symbolKinds,
        relationshipTypes,
        graphDirection,
        depth,
        includeIsolated ?? true);
    GraphSelection? selection = null;
    if (!string.IsNullOrWhiteSpace(selected))
    {
        try
        {
            selection = new GraphSelection(selected.Trim());
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    var options = new GraphQueryOptions(scanId, filters, selection)
    {
        ProjectId = projectId
    };
    var graph = await graphQuery.GetAsync(options, cancellationToken);

    return Results.Ok(new
    {
        scanId = graph.ScanId,
        projectId = graph.ProjectId,
        state = graph.State.ToString(),
        statusMessage = graph.StatusMessage,
        selectedNodeId = graph.SelectedNodeId,
        totalNodeCount = graph.TotalNodeCount,
        totalEdgeCount = graph.TotalEdgeCount,
        visibleNodeCount = graph.VisibleNodeCount,
        visibleEdgeCount = graph.VisibleEdgeCount,
        analysisStatus = graph.AnalysisStatus?.ToString(),
        analysisCompletedAtUtc = graph.AnalysisCompletedAtUtc,
        persistedNodeCount = graph.PersistedNodeCount,
        persistedEdgeCount = graph.PersistedEdgeCount,
        validEdgeCount = graph.ValidEdgeCount,
        unresolvedDependencyCount = graph.UnresolvedDependencyCount,
        missingSourceFileCount = graph.MissingSourceFileCount,
        duplicateEdgeCount = graph.DuplicateEdgeCount,
        selfEdgeCount = graph.SelfEdgeCount,
        nodes = graph.Nodes.Select(node => new
        {
            id = node.Id,
            label = node.Label,
            kind = node.Kind.ToString(),
            symbolId = node.SymbolId,
            symbolKey = node.SymbolKey,
            fullyQualifiedName = node.FullyQualifiedName,
            namespaceName = node.Namespace,
            containingType = node.ContainingType,
            scanId = node.ScanId,
            relativePath = node.RelativePath,
            startLine = node.StartLine,
            endLine = node.EndLine,
            x = node.X,
            y = node.Y,
            isSelected = node.IsSelected,
            isNeighbor = node.IsNeighbor,
            isHighlighted = node.IsHighlighted,
            isIsolated = node.IsIsolated,
            incomingDependencyCount = node.IncomingDependencyCount,
            outgoingDependencyCount = node.OutgoingDependencyCount,
            relatedFindingCount = node.RelatedFindingCount
        }).ToArray(),
        edges = graph.Edges.Select(edge => new
        {
            fromId = edge.FromId,
            toId = edge.ToId,
            relationshipType = edge.RelationshipType,
            isResolved = edge.IsResolved,
            isHighlighted = edge.IsHighlighted,
            isIncoming = edge.IsIncoming,
            isOutgoing = edge.IsOutgoing
        }).ToArray()
    });
});

app.MapGet("/api/projects/{projectId:guid}/compare", async (
    Guid projectId,
    Guid baselineScanId,
    Guid currentScanId,
    ForgeDeskDbContext db,
    IAnalysisComparisonService comparison,
    CancellationToken cancellationToken) =>
{
    var projectExists = await db.Projects
        .AsNoTracking()
        .AnyAsync(project => project.Id == projectId, cancellationToken);
    if (!projectExists)
    {
        return Results.NotFound(new { error = $"Project '{projectId}' was not found." });
    }

    var ownedScanIds = await db.Scans
        .AsNoTracking()
        .Where(scan => scan.ProjectId == projectId
            && (scan.Id == baselineScanId || scan.Id == currentScanId))
        .Select(scan => scan.Id)
        .ToListAsync(cancellationToken);
    if (!ownedScanIds.Contains(baselineScanId) || !ownedScanIds.Contains(currentScanId))
    {
        return Results.NotFound(new { error = "Both comparison scans must belong to the selected project." });
    }

    try
    {
        var result = await comparison.CompareAsync(
            projectId,
            baselineScanId,
            currentScanId,
            cancellationToken: cancellationToken);
        return Results.Ok(result);
    }
    catch (ProjectValidationException exception)
    {
        return Results.BadRequest(new { error = exception.Message, errors = exception.Errors });
    }
    catch (EntityNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
});

app.MapGet("/api/projects/{projectId:guid}/scans/{scanId:guid}/source", async (
    Guid projectId,
    Guid scanId,
    string? path,
    int? startLine,
    int? endLine,
    ForgeDeskDbContext db,
    CancellationToken cancellationToken) =>
{
    var project = await db.Projects
        .AsNoTracking()
        .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken);
    var scan = await FindOwnedScanAsync(db, projectId, scanId, cancellationToken);
    if (project is null || scan is null)
    {
        return Results.NotFound(new { error = "The selected project or scan was not found." });
    }

    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { error = "A relative source path is required." });
    }

    var normalizedRoot = PathNormalization.Normalize(project.RootPath);
    var candidate = PathNormalization.Normalize(Path.Combine(normalizedRoot, path.Trim()));
    if (!PathNormalization.IsWithin(candidate, normalizedRoot))
    {
        return Results.BadRequest(new { error = "The source path must remain inside the registered project root." });
    }

    if (!File.Exists(candidate))
    {
        return Results.NotFound(new { error = "The requested source file no longer exists." });
    }

    try
    {
        var rootInfo = new DirectoryInfo(normalizedRoot);
        var physicalRoot = rootInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? rootInfo.FullName;
        var fileInfo = new FileInfo(candidate);
        var physicalFile = fileInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fileInfo.FullName;
        if (!PathNormalization.IsWithin(physicalFile, physicalRoot))
        {
            return Results.BadRequest(new { error = "The source file is outside the registered project root." });
        }

        if (fileInfo.Length > 2 * 1024 * 1024)
        {
            return Results.BadRequest(new { error = "Source previews are limited to files smaller than 2 MiB." });
        }

        var lines = await File.ReadAllLinesAsync(candidate, cancellationToken);
        var firstLine = Math.Clamp(startLine ?? 1, 1, Math.Max(lines.Length, 1));
        var lastLine = Math.Clamp(endLine ?? firstLine + 80, firstLine, Math.Max(lines.Length, firstLine));
        var preview = lines
            .Skip(firstLine - 1)
            .Take(Math.Min(lastLine - firstLine + 1, 160))
            .Select((text, offset) => new
            {
                lineNumber = firstLine + offset,
                text
            })
            .ToArray();

        return Results.Ok(new
        {
            relativePath = path.Trim(),
            totalLines = lines.Length,
            startLine = firstLine,
            endLine = firstLine + preview.Length - 1,
            lines = preview
        });
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (IOException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/api/projects/{projectId:guid}/git", async (
    Guid projectId,
    ForgeDeskDbContext db,
    IGitRepositoryService git,
    CancellationToken cancellationToken) =>
{
    var project = await db.Projects
        .AsNoTracking()
        .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken);
    if (project is null)
    {
        return Results.NotFound(new { error = $"Project '{projectId}' was not found." });
    }

    var snapshot = await git.InspectAsync(
        project.RootPath,
        new GitInspectionOptions
        {
            MaxChangedFiles = 2_000,
            MaxRecentCommits = 15,
            IncludeHistory = true
        },
        cancellationToken);

    return Results.Ok(new
    {
        state = snapshot.State.ToString(),
        repositoryRootPath = snapshot.RepositoryRootPath,
        branch = snapshot.Branch,
        headCommit = snapshot.HeadCommit,
        changes = snapshot.Changes,
        recentCommits = snapshot.RecentCommits,
        errorMessage = snapshot.ErrorMessage,
        changesTruncated = snapshot.ChangesTruncated,
        historyTruncated = snapshot.HistoryTruncated
    });
});

app.MapGet("/api/projects/{projectId:guid}/scans/{scanId:guid}/reports/{format}", async (
    Guid projectId,
    Guid scanId,
    string format,
    ForgeDeskDbContext db,
    IAnalysisReportService reports,
    CancellationToken cancellationToken) =>
{
    var project = await db.Projects
        .AsNoTracking()
        .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken);
    var scan = await FindOwnedScanAsync(db, projectId, scanId, cancellationToken);
    if (project is null || scan is null)
    {
        return Results.NotFound(new { error = "The selected project or scan was not found." });
    }

    if (!Enum.TryParse<AnalysisReportFormat>(format, true, out var reportFormat))
    {
        return Results.BadRequest(new { error = "Report format must be json, html, or csv." });
    }

    try
    {
        var report = await reports.BuildAsync(
            projectId,
            scanId,
            new AnalysisReportOptions
            {
                IncludeAbsolutePaths = false,
                IncludeGitContext = true
            },
            cancellationToken);
        var content = reportFormat switch
        {
            AnalysisReportFormat.Json => AnalysisReportSerializer.ToJson(report),
            AnalysisReportFormat.Html => AnalysisReportSerializer.ToHtml(report),
            AnalysisReportFormat.Csv => AnalysisReportSerializer.ToCsv(report),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var contentType = reportFormat switch
        {
            AnalysisReportFormat.Json => "application/json; charset=utf-8",
            AnalysisReportFormat.Html => "text/html; charset=utf-8",
            AnalysisReportFormat.Csv => "text/csv; charset=utf-8",
            _ => "application/octet-stream"
        };
        var fileName = AnalysisReportFileNames.Create(project.Name, scanId, reportFormat);
        return Results.File(Encoding.UTF8.GetBytes(content), contentType, fileName);
    }
    catch (EntityNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (Exception exception) when (exception is InvalidOperationException or IOException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

// Keep unmatched API requests JSON-shaped. Without this boundary, the SPA fallback
// can return index.html for a stale/unknown API route, which makes browser clients
// fail with: Unexpected token '<' while parsing JSON.
app.MapFallback("/api/{**path}", () => Results.NotFound(new
{
    error = "The requested ForgeDesk API endpoint was not found. Restart the web host after rebuilding it."
}));
app.MapFallbackToFile("index.html");

app.Run();

static async Task<Scan?> FindOwnedScanAsync(
    ForgeDeskDbContext db,
    Guid projectId,
    Guid scanId,
    CancellationToken cancellationToken)
    => await db.Scans
        .AsNoTracking()
        .SingleOrDefaultAsync(
            scan => scan.Id == scanId && scan.ProjectId == projectId,
            cancellationToken);

static IReadOnlyCollection<string>? SplitFilter(string? value)
{
    var values = value?
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(item => item.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    return values is { Length: > 0 } ? values : null;
}

static bool TryParseGraphDirection(string? value, out GraphDirection direction, out string? error)
{
    direction = GraphDirection.Both;
    error = null;
    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    if (Enum.TryParse(value.Trim(), true, out direction) && Enum.IsDefined(direction))
    {
        return true;
    }

    error = $"Unknown graph direction '{value}'. Use Both, Incoming, or Outgoing.";
    return false;
}

static bool TryParseEnumList<TEnum>(
    string? value,
    out IReadOnlyCollection<TEnum>? parsed,
    out string? error)
    where TEnum : struct, Enum
{
    parsed = null;
    error = null;
    var values = SplitFilter(value);
    if (values is null)
    {
        return true;
    }

    var output = new List<TEnum>(values.Count);
    foreach (var item in values)
    {
        if (!Enum.TryParse<TEnum>(item, true, out var parsedValue) || !Enum.IsDefined(parsedValue))
        {
            error = $"Unknown {typeof(TEnum).Name} value '{item}'.";
            return false;
        }

        output.Add(parsedValue);
    }

    parsed = output;
    return true;
}
