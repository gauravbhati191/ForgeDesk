using System.Security.Cryptography;
using System.Text;
using ForgeDesk.Application;
using ForgeDesk.Domain;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

using DomainSymbolKind = ForgeDesk.Domain.SymbolKind;
using RoslynSymbolKind = Microsoft.CodeAnalysis.SymbolKind;
using ApplicationAnalysisFileInput = ForgeDesk.Application.AnalysisFileInput;

namespace ForgeDesk.Analysis;

/// <summary>
/// Roslyn-backed C# analyzer. It first attempts to load the registered project or
/// solution through MSBuildWorkspace. When that cannot be done, it reports an
/// explicit syntax-only fallback instead of pretending that an AdhocWorkspace is
/// equivalent to project-aware semantic loading.
/// </summary>
/// <remarks>Dependency extraction currently persists type references, calls, inheritance, and interface implementation edges.</remarks>
public sealed class RoslynAnalysisEngine : ForgeDesk.Application.IAnalysisEngine, ForgeDesk.Analysis.IAnalysisEngine
{
    private static readonly object MsBuildRegistrationLock = new();
    private const string TodoRuleId = AnalysisRuleIds.TodoOrFixme;
    private const string LongMethodRuleId = AnalysisRuleIds.LongMethod;
    private const string ExcessiveParametersRuleId = AnalysisRuleIds.ExcessiveParameters;
    private const string EmptyCatchRuleId = AnalysisRuleIds.EmptyCatch;

    public Task<ForgeDesk.Application.AnalysisResult> AnalyzeAsync(
        ForgeDesk.Application.AnalysisRequest request,
        CancellationToken cancellationToken = default)
        => AnalyzeAsync(request, progress: null, cancellationToken);

    public async Task<ForgeDesk.Application.AnalysisResult> AnalyzeAsync(
        ForgeDesk.Application.AnalysisRequest request,
        IProgress<ForgeDesk.Application.AnalysisProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ScanId == Guid.Empty)
        {
            throw new ArgumentException("An analysis scan identifier is required.", nameof(request));
        }

        if (request.Files is null)
        {
            throw new ArgumentException("Analysis source files are required.", nameof(request));
        }

        var options = request.QualityOptions ?? new AnalysisQualityOptions();
        if (options.MaxMethodLines < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxMethodLines), "The maximum method length must be positive.");
        }

        if (options.MaxParameters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxParameters), "The maximum parameter count cannot be negative.");
        }

        var inputs = request.Files
            .Where(file => file is not null)
            .OrderBy(file => file.NormalizedPath, StringComparer.Ordinal)
            .ThenBy(file => file.SourceFileId)
            .ToArray();
        var inputByPath = inputs.ToDictionary(file => NormalizePath(file.NormalizedPath), StringComparer.Ordinal);
        var fileIdsByPath = inputByPath.ToDictionary(pair => pair.Key, pair => pair.Value.SourceFileId, StringComparer.Ordinal);

        progress?.Report(new AnalysisProgress("Parsing", 0, inputs.Length, 0, 0, 0));

        var workspaceSnapshot = await LoadWorkspaceAsync(request, inputs, inputByPath, cancellationToken).ConfigureAwait(false);
        var files = workspaceSnapshot.Files;
        var compilations = workspaceSnapshot.Compilations;
        progress?.Report(new AnalysisProgress(
            "Binding",
            0,
            inputs.Length,
            0,
            0,
            0,
            BindingMode: workspaceSnapshot.BindingMode,
            BindingMessage: workspaceSnapshot.BindingMessage));

        var declarations = new List<DeclaredSymbol>();
        var declarationsByIdentity = new Dictionary<string, List<DeclaredSymbol>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var node in file.Tree.GetRoot(cancellationToken).DescendantNodesAndSelf())
            {
                if (!IsSupportedDeclaration(node))
                {
                    continue;
                }

                var symbol = file.Model?.GetDeclaredSymbol(node, cancellationToken);
                if (symbol is null && file.Model is not null)
                {
                    continue;
                }

                if (symbol?.Kind == RoslynSymbolKind.ErrorType)
                {
                    continue;
                }

                var identity = symbol is null ? SyntaxSymbolIdentity(node) : SymbolIdentity(symbol);
                var key = CreateSymbolKey(request.ScanId, file.Input.SourceFileId, identity);
                var declaration = new DeclaredSymbol(
                    key,
                    identity,
                    file.Input.SourceFileId,
                    file.Path,
                    symbol?.Name ?? SyntaxSymbolName(node),
                    symbol is null ? SyntaxSymbolKind(node) : MapSymbolKind(symbol),
                    symbol,
                    node);
                if (declarations.Any(existing => StringComparer.Ordinal.Equals(existing.Key, key)))
                {
                    continue;
                }

                declarations.Add(declaration);
                if (!declarationsByIdentity.TryGetValue(identity, out var matches))
                {
                    matches = [];
                    declarationsByIdentity.Add(identity, matches);
                }

                matches.Add(declaration);
            }
        }

        declarations.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        foreach (var matches in declarationsByIdentity.Values)
        {
            matches.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        }

        var symbols = declarations
            .Select(declaration => new ForgeDesk.Application.AnalysisSymbolResult(
                declaration.SourceFileId,
                declaration.Key,
                declaration.Name,
                declaration.Kind,
                StartLine(declaration.Node),
                EndLine(declaration.Node)))
            .ToList();
        var dependencies = new List<ForgeDesk.Application.AnalysisDependencyResult>();
        var dependencyKeys = new HashSet<DependencyIdentity>(DependencyIdentityComparer.Instance);
        var findings = new List<ForgeDesk.Application.AnalysisFindingResult>();

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[fileIndex];
            var root = file.Tree.GetRoot(cancellationToken);

            if (file.Model is not null)
            {
                foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (typeSyntax.Ancestors().Any(ancestor => ancestor is BaseTypeSyntax))
                    {
                        continue;
                    }

                    var target = file.Model.GetTypeInfo(typeSyntax, cancellationToken).Type
                        ?? file.Model.GetSymbolInfo(typeSyntax, cancellationToken).Symbol;
                    AddDependency(
                        file,
                        typeSyntax,
                        target,
                        DependencyKind.TypeReference.ToString(),
                        dependencies,
                        dependencyKeys,
                        declarationsByIdentity,
                        fileIdsByPath);
                }

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = file.Model.GetSymbolInfo(invocation.Expression, cancellationToken).Symbol;
                    AddDependency(
                        file,
                        invocation,
                        target,
                        DependencyKind.Calls.ToString(),
                        dependencies,
                        dependencyKeys,
                        declarationsByIdentity,
                        fileIdsByPath);
                }

                foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = file.Model.GetSymbolInfo(creation.Type, cancellationToken).Symbol
                        ?? file.Model.GetTypeInfo(creation.Type, cancellationToken).Type;
                    AddDependency(
                        file,
                        creation,
                        target,
                        DependencyKind.Calls.ToString(),
                        dependencies,
                        dependencyKeys,
                        declarationsByIdentity,
                        fileIdsByPath);
                }

                foreach (var baseType in root.DescendantNodes().OfType<BaseTypeSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = file.Model.GetTypeInfo(baseType.Type, cancellationToken).Type
                        ?? file.Model.GetSymbolInfo(baseType.Type, cancellationToken).Symbol;
                    var relationship = target is INamedTypeSymbol named && named.TypeKind == TypeKind.Interface
                        ? DependencyKind.Implements.ToString()
                        : DependencyKind.Inherits.ToString();
                    AddDependency(
                        file,
                        baseType,
                        target,
                        relationship,
                        dependencies,
                        dependencyKeys,
                        declarationsByIdentity,
                        fileIdsByPath);
                }
            }

            if (options.EnableTodoCommentRule && options.IsRuleEnabled(TodoRuleId))
            {
                AddTodoFindings(file, root, findings, declarationsByIdentity, fileIdsByPath, cancellationToken);
            }

            if (options.EnableLongMethodRule && options.IsRuleEnabled(LongMethodRuleId))
            {
                AddLongMethodFindings(file, root, options.MaxMethodLines, findings, declarationsByIdentity, fileIdsByPath, cancellationToken);
            }

            if (options.EnableExcessiveParametersRule && options.IsRuleEnabled(ExcessiveParametersRuleId))
            {
                AddExcessiveParameterFindings(file, root, options.MaxParameters, findings, declarationsByIdentity, fileIdsByPath, cancellationToken);
            }

            if (options.EnableEmptyCatchRule && options.IsRuleEnabled(EmptyCatchRuleId))
            {
                AddEmptyCatchFindings(file, root, findings, declarationsByIdentity, fileIdsByPath, cancellationToken);
            }

            progress?.Report(new AnalysisProgress(
                "Binding",
                fileIndex + 1,
                inputs.Length,
                symbols.Count,
                dependencies.Count,
                findings.Count,
                file.Input.RelativePath));
        }

        if (options.IncludeCompilerDiagnostics && compilations.Count > 0)
        {
            AddCompilerFindings(compilations, findings, files, declarationsByIdentity, fileIdsByPath, cancellationToken);
        }
        else if (options.IncludeCompilerDiagnostics)
        {
            AddSyntaxDiagnostics(files, findings, declarationsByIdentity, fileIdsByPath, cancellationToken);
        }

        findings = findings.Distinct().ToList();
        dependencies.Sort(CompareDependencies);
        findings.Sort(CompareFindings);
        progress?.Report(new AnalysisProgress(
            "Rules",
            inputs.Length,
            inputs.Length,
            symbols.Count,
            dependencies.Count,
            findings.Count));

        return new ForgeDesk.Application.AnalysisResult(
            symbols,
            dependencies,
            findings,
            workspaceSnapshot.BindingMode,
            workspaceSnapshot.BindingMessage);
    }

    /// <summary>Compatibility overload for the Phase 1 analysis contract.</summary>
    public async Task<ForgeDesk.Analysis.AnalysisResult> AnalyzeAsync(
        ForgeDesk.Analysis.AnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var firstPath = request.Files.FirstOrDefault()?.NormalizedPath;
        var rootPath = firstPath is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(firstPath) ?? Directory.GetCurrentDirectory();
        var files = request.Files
            .Select(file => new ForgeDesk.Application.AnalysisFileInput(
                file.SourceFileId,
                Path.GetFileName(file.NormalizedPath),
                file.NormalizedPath,
                file.Content))
            .ToArray();
        var result = await AnalyzeAsync(
            new ForgeDesk.Application.AnalysisRequest(
                request.ScanId,
                Guid.NewGuid(),
                rootPath,
                files),
            cancellationToken).ConfigureAwait(false);

        return new ForgeDesk.Analysis.AnalysisResult(
            result.Symbols
                .Select(symbol => new ForgeDesk.Analysis.AnalysisSymbolDraft(
                    symbol.Name,
                    symbol.Kind,
                    symbol.StartLine ?? 1,
                    0,
                    symbol.SymbolKey))
                .ToArray(),
            result.Findings
                .Select(finding => new ForgeDesk.Analysis.AnalysisFindingDraft(
                    finding.RuleId,
                    finding.Severity,
                    finding.Message,
                    finding.SourceFileId,
                    finding.StartLine,
                    null))
                .ToArray());
    }

    private static async Task<WorkspaceSnapshot> LoadWorkspaceAsync(
        ForgeDesk.Application.AnalysisRequest request,
        IReadOnlyList<ApplicationAnalysisFileInput> inputs,
        IReadOnlyDictionary<string, ApplicationAnalysisFileInput> inputByPath,
        CancellationToken cancellationToken)
    {
        var workspacePath = FindWorkspacePath(request.ProjectRootPath);
        if (workspacePath is null)
        {
            return CreateSyntaxOnlySnapshot(
                inputs,
                $"No .sln, .slnx, or .csproj was found below '{request.ProjectRootPath}'.");
        }

        try
        {
            EnsureMsBuildRegistered();

            using var workspace = MSBuildWorkspace.Create();
            var workspaceDiagnostics = new List<string>();
            workspace.WorkspaceFailed += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Diagnostic.Message))
                {
                    workspaceDiagnostics.Add(args.Diagnostic.Message.Trim());
                }
            };

            var extension = Path.GetExtension(workspacePath);
            var solution = extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                ? await workspace.OpenSolutionAsync(workspacePath, cancellationToken: cancellationToken).ConfigureAwait(false)
                : (await workspace.OpenProjectAsync(workspacePath, cancellationToken: cancellationToken).ConfigureAwait(false)).Solution;

            var files = new List<FileContext>(inputs.Count);
            var compilations = new List<CSharpCompilation>();
            var seenSourceFiles = new HashSet<(Guid SourceFileId, string Path)>();
            var projects = solution.Projects
                .Where(project => project.Language == LanguageNames.CSharp)
                .OrderBy(project => project.FilePath ?? project.Name, StringComparer.Ordinal)
                .ToArray();

            foreach (var project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not CSharpCompilation compilation)
                {
                    continue;
                }

                compilations.Add(compilation);
                foreach (var document in project.Documents.OrderBy(item => item.FilePath ?? item.Name, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(document.FilePath))
                    {
                        continue;
                    }

                    var path = NormalizePath(document.FilePath);
                    if (!inputByPath.TryGetValue(path, out var input)
                        || !seenSourceFiles.Add((input.SourceFileId, path)))
                    {
                        continue;
                    }

                    var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"MSBuildWorkspace did not produce a syntax tree for '{input.RelativePath}'.");
                    files.Add(new FileContext(input, tree, compilation.GetSemanticModel(tree, ignoreAccessibility: false)));
                }
            }

            if (files.Count == 0)
            {
                throw new InvalidOperationException($"MSBuildWorkspace loaded '{Path.GetFileName(workspacePath)}' but none of the discovered source files belonged to a C# project.");
            }

            var matchedIds = files.Select(file => file.Input.SourceFileId).ToHashSet();
            foreach (var input in inputs.Where(item => !matchedIds.Contains(item.SourceFileId)))
            {
                files.Add(CreateSyntaxOnlyFile(input));
            }

            var bindingMessage = workspaceDiagnostics.Count == 0
                ? null
                : $"MSBuildWorkspace reported: {string.Join(" | ", workspaceDiagnostics.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).Take(8))}";
            if (files.Count != inputs.Count)
            {
                bindingMessage = string.IsNullOrWhiteSpace(bindingMessage)
                    ? "Some discovered files were not part of the loaded MSBuild graph and were analyzed with syntax-only fallback."
                    : $"{bindingMessage} Some discovered files were analyzed with syntax-only fallback.";
            }

            return new WorkspaceSnapshot(files, compilations, AnalysisBindingMode.MsBuildSemantic, bindingMessage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CreateSyntaxOnlySnapshot(
                inputs,
                $"MSBuildWorkspace could not load '{Path.GetFileName(workspacePath)}'; syntax-only fallback was used. {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static WorkspaceSnapshot CreateSyntaxOnlySnapshot(
        IReadOnlyList<ApplicationAnalysisFileInput> inputs,
        string message)
        => new(
            inputs.Select(CreateSyntaxOnlyFile).ToArray(),
            [],
            AnalysisBindingMode.SyntaxOnlyFallback,
            message);

    private static FileContext CreateSyntaxOnlyFile(ApplicationAnalysisFileInput input)
    {
        var tree = CSharpSyntaxTree.ParseText(
            SourceText.From(input.Content ?? string.Empty, Encoding.UTF8),
            ParseOptions(),
            NormalizePath(input.NormalizedPath));
        return new FileContext(input, tree, Model: null);
    }

    private static CSharpParseOptions ParseOptions()
        => new(LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Regular);

    private static string? FindWorkspacePath(string rootPath)
    {
        try
        {
            var root = NormalizePath(rootPath);
            if (!Directory.Exists(root))
            {
                return null;
            }

            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path =>
                    Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => WorkspacePathPriority(Path.GetExtension(path)))
                .ThenBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static int WorkspacePathPriority(string extension)
        => extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? 0
            : extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ? 1
            : 2;

    private static void EnsureMsBuildRegistered()
    {
        lock (MsBuildRegistrationLock)
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
        }
    }

    private static void AddDependency(
        FileContext file,
        SyntaxNode node,
        ISymbol? target,
        string relationship,
        ICollection<ForgeDesk.Application.AnalysisDependencyResult> dependencies,
        ISet<DependencyIdentity> dependencyKeys,
        IReadOnlyDictionary<string, List<DeclaredSymbol>> declarationsByIdentity,
        IReadOnlyDictionary<string, Guid> fileIdsByPath)
    {
        var source = FindEnclosingDeclaration(file, node, declarationsByIdentity, fileIdsByPath);
        if (source is null || target is null)
        {
            return;
        }

        var targetIdentity = SymbolIdentity(target);
        var targetKey = FindTargetKey(target, targetIdentity, declarationsByIdentity, fileIdsByPath);
        var isUnresolved = targetKey is null && target is IErrorTypeSymbol;
        if (targetKey is null && !isUnresolved)
        {
            // Resolved framework/project-external symbols are intentionally not persisted
            // because Dependency targets must point at symbols in this scan.
            return;
        }

        if (StringComparer.Ordinal.Equals(source.Key, targetKey))
        {
            return;
        }

        var identity = new DependencyIdentity(source.Key, targetKey, relationship);
        if (!dependencyKeys.Add(identity))
        {
            return;
        }

        dependencies.Add(new ForgeDesk.Application.AnalysisDependencyResult(
            source.Key,
            targetKey,
            relationship,
            targetKey is not null));
    }

    private static void AddTodoFindings(
        FileContext file,
        SyntaxNode root,
        ICollection<ForgeDesk.Application.AnalysisFindingResult> findings,
        IReadOnlyDictionary<string, List<DeclaredSymbol>> declarationsByIdentity,
        IReadOnlyDictionary<string, Guid> fileIdsByPath,
        CancellationToken cancellationToken)
    {
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (trivia.Kind() is not (SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia))
            {
                continue;
            }

            var text = trivia.ToString();
            var todoIndex = text.IndexOf("TODO", StringComparison.OrdinalIgnoreCase);
            var fixmeIndex = text.IndexOf("FIXME", StringComparison.OrdinalIgnoreCase);
            if (todoIndex < 0 && fixmeIndex < 0)
            {
                continue;
            }

            var marker = todoIndex >= 0 && (fixmeIndex < 0 || todoIndex < fixmeIndex) ? "TODO" : "FIXME";

            var symbol = FindEnclosingDeclaration(file, trivia.Token.Parent ?? root, declarationsByIdentity, fileIdsByPath);
            var message = text.Trim();
            if (message.Length > 4000)
            {
                message = message[..4000];
            }

            findings.Add(new ForgeDesk.Application.AnalysisFindingResult(
                TodoRuleId,
                FindingSeverity.Warning,
                $"{marker} comment requires attention: {message}",
                file.Input.SourceFileId,
                symbol?.Key,
                StartLine(trivia),
                StartLine(trivia)));
        }
    }

    private static void AddExcessiveParameterFindings(
        FileContext file,
        SyntaxNode root,
        int maxParameters,
        ICollection<ForgeDesk.Application.AnalysisFindingResult> findings,
        IReadOnlyDictionary<string, List<DeclaredSymbol>> declarationsByIdentity,
        IReadOnlyDictionary<string, Guid> fileIdsByPath,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().Where(node => node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameterCount = declaration switch
            {
                BaseMethodDeclarationSyntax method => method.ParameterList.Parameters.Count,
                LocalFunctionStatementSyntax localFunction => localFunction.ParameterList.Parameters.Count,
                _ => 0
            };
            if (parameterCount <= maxParameters)
            {
                continue;
            }

            var symbol = FindEnclosingDeclaration(file, declaration, declarationsByIdentity, fileIdsByPath);
            findings.Add(new ForgeDesk.Application.AnalysisFindingResult(
                ExcessiveParametersRuleId,
                FindingSeverity.Warning,
                $"Method declares {parameterCount} parameters, exceeding the configured limit of {maxParameters}.",
                file.Input.SourceFileId,
                symbol?.Key,
                StartLine(declaration),
                EndLine(declaration)));
        }
    }

    private static void AddEmptyCatchFindings(
        FileContext file,
        SyntaxNode root,
        ICollection<ForgeDesk.Application.AnalysisFindingResult> findings,
        IReadOnlyDictionary<string, List<DeclaredSymbol>> declarationsByIdentity,
        IReadOnlyDictionary<string, Guid> fileIdsByPath,
        CancellationToken cancellationToken)
    {
        foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (catchClause.Block is null || catchClause.Block.Statements.Count != 0)
            {
                continue;
            }

            var symbol = FindEnclosingDeclaration(file, catchClause, declarationsByIdentity, fileIdsByPath);
            findings.Add(new ForgeDesk.Application.AnalysisFindingResult(
                EmptyCatchRuleId,
                FindingSeverity.Warning,
                "Catch block is empty and silently discards the exception.",
                file.Input.SourceFileId,
                symbol?.Key,
                StartLine(catchClause),
                EndLine(catchClause)));
        }
    }

    private static void AddLongMethodFindings(
        FileContext file,
        SyntaxNode root,
        int maxMethodLines,
        ICollection<ForgeDesk.Application.AnalysisFindingResult> findings,
        IReadOnlyDictionary<string, List<DeclaredSymbol>> declarationsByIdentity,
        IReadOnlyDictionary<string, Guid> fileIdsByPath,
        CancellationToken cancellationToken)
    {
        foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startLine = StartLine(method);
            var endLine = EndLine(method);
            if (startLine is null || endLine is null || endLine.Value - startLine.Value + 1 <= maxMethodLines)
            {
                continue;
            }

            var symbol = FindEnclosingDeclaration(file, method, declarationsByIdentity, fileIdsByPath);
            findings.Add(new ForgeDesk.Application.AnalysisFindingResult(
                LongMethodRuleId,
                FindingSeverity.Warning,
                $"Method spans {endLine.Value - startLine.Value + 1} lines, exceeding the configured limit of {maxMethodLines}.",
                file.Input.SourceFileId,
                symbol?.Key,
                startLine,
                endLine));
        }
    }

    private static void AddCompilerFindings(
        IReadOnlyList<CSharpCompilation> compilations,
        ICollection<ForgeDesk.Application.AnalysisFindingResult> findings,
        IReadOnlyList<FileContext> files,
        IReadOnlyDictionary<string, List<DeclaredSymbol>> declarationsByIdentity,
        IReadOnlyDictionary<string, Guid> fileIdsByPath,
        CancellationToken cancellationToken)
    {
        foreach (var diagnostic in compilations
            .SelectMany(compilation => compilation.GetDiagnostics(cancellationToken))
            .Where(item => item.Severity != DiagnosticSeverity.Hidden)
            .OrderBy(item => item.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Location.SourceSpan.Start)
            .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree is null)
            {
                continue;
            }

            var path = NormalizePath(diagnostic.Location.SourceTree.FilePath ?? string.Empty);
            var file = files.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Path, path));
            if (file is null || !fileIdsByPath.TryGetValue(path, out var sourceFileId))
            {
                continue;
            }

            var lineSpan = diagnostic.Location.GetLineSpan();
            var startLine = lineSpan.StartLinePosition.Line + 1;
            var endLine = lineSpan.EndLinePosition.Line + 1;
            var symbol = FindEnclosingDeclaration(file, file.Tree.GetRoot(cancellationToken).FindNode(diagnostic.Location.SourceSpan), declarationsByIdentity, fileIdsByPath);
            var severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => FindingSeverity.Error,
                DiagnosticSeverity.Warning => FindingSeverity.Warning,
                _ => FindingSeverity.Info
            };
            findings.Add(new ForgeDesk.Application.AnalysisFindingResult(
                diagnostic.Id,
                severity,
                diagnostic.GetMessage(),
                sourceFileId,
                symbol?.Key,
                startLine,
                endLine));
        }
    }

    private static void AddSyntaxDiagnostics(
        IReadOnlyList<FileContext> files,
        ICollection<ForgeDesk.Application.AnalysisFindingResult> findings,
        IReadOnlyDictionary<string, List<DeclaredSymbol>> declarationsByIdentity,
        IReadOnlyDictionary<string, Guid> fileIdsByPath,
        CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            foreach (var diagnostic in file.Tree.GetDiagnostics(cancellationToken)
                .Where(item => item.Severity != DiagnosticSeverity.Hidden)
                .OrderBy(item => item.Location.SourceSpan.Start)
                .ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddDiagnosticFinding(
                    diagnostic,
                    file,
                    findings,
                    declarationsByIdentity,
                    fileIdsByPath,
                    cancellationToken);
            }
        }
    }

    private static void AddDiagnosticFinding(
        Diagnostic diagnostic,
        FileContext file,
        ICollection<ForgeDesk.Application.AnalysisFindingResult> findings,
        IReadOnlyDictionary<string, List<DeclaredSymbol>> declarationsByIdentity,
        IReadOnlyDictionary<string, Guid> fileIdsByPath,
        CancellationToken cancellationToken)
    {
        if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree is null)
        {
            return;
        }

        var lineSpan = diagnostic.Location.GetLineSpan();
        var startLine = lineSpan.StartLinePosition.Line + 1;
        var endLine = lineSpan.EndLinePosition.Line + 1;
        var symbol = FindEnclosingDeclaration(
            file,
            file.Tree.GetRoot(cancellationToken).FindNode(diagnostic.Location.SourceSpan),
            declarationsByIdentity,
            fileIdsByPath);
        var severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => FindingSeverity.Error,
            DiagnosticSeverity.Warning => FindingSeverity.Warning,
            _ => FindingSeverity.Info
        };
        findings.Add(new ForgeDesk.Application.AnalysisFindingResult(
            diagnostic.Id,
            severity,
            diagnostic.GetMessage(),
            file.Input.SourceFileId,
            symbol?.Key,
            startLine,
            endLine));
    }

    private static DeclaredSymbol? FindEnclosingDeclaration(
        FileContext file,
        SyntaxNode node,
        IReadOnlyDictionary<string, List<DeclaredSymbol>> declarationsByIdentity,
        IReadOnlyDictionary<string, Guid> fileIdsByPath)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (!IsSupportedDeclaration(current))
            {
                continue;
            }

            var symbol = file.Model?.GetDeclaredSymbol(current);
            if (symbol is null && file.Model is not null)
            {
                continue;
            }

            var identity = symbol is null ? SyntaxSymbolIdentity(current) : SymbolIdentity(symbol);
            if (!declarationsByIdentity.TryGetValue(identity, out var declarations))
            {
                continue;
            }

            var candidate = declarations.FirstOrDefault(item => item.SourceFileId == file.Input.SourceFileId);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string SyntaxSymbolIdentity(SyntaxNode node)
    {
        var containers = node.Ancestors()
            .Reverse()
            .Where(item => item is NamespaceDeclarationSyntax
                or FileScopedNamespaceDeclarationSyntax
                or BaseTypeDeclarationSyntax)
            .Select(SyntaxContainerName)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        var containingName = containers.Length == 0 ? string.Empty : $"{string.Join('.', containers)}.";
        var parameterSignature = node switch
        {
            BaseMethodDeclarationSyntax method => ParameterSignature(method.ParameterList),
            LocalFunctionStatementSyntax localFunction => ParameterSignature(localFunction.ParameterList),
            DelegateDeclarationSyntax @delegate => ParameterSignature(@delegate.ParameterList),
            IndexerDeclarationSyntax indexer => ParameterSignature(indexer.ParameterList),
            _ => string.Empty
        };
        var arity = node switch
        {
            TypeDeclarationSyntax type => TypeParameterSuffix(type.TypeParameterList),
            MethodDeclarationSyntax method => TypeParameterSuffix(method.TypeParameterList),
            DelegateDeclarationSyntax @delegate => TypeParameterSuffix(@delegate.TypeParameterList),
            LocalFunctionStatementSyntax localFunction => TypeParameterSuffix(localFunction.TypeParameterList),
            _ => string.Empty
        };
        return $"{SyntaxSymbolKind(node)}:{containingName}{SyntaxSymbolName(node)}{arity}{parameterSignature}";
    }

    private static string SyntaxContainerName(SyntaxNode node)
        => node switch
        {
            NamespaceDeclarationSyntax @namespace => @namespace.Name.ToString(),
            FileScopedNamespaceDeclarationSyntax @namespace => @namespace.Name.ToString(),
            TypeDeclarationSyntax type => $"{type.Identifier.ValueText}{TypeParameterSuffix(type.TypeParameterList)}",
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            _ => string.Empty
        };

    private static string SyntaxSymbolName(SyntaxNode node)
        => node switch
        {
            NamespaceDeclarationSyntax @namespace => @namespace.Name.ToString(),
            FileScopedNamespaceDeclarationSyntax @namespace => @namespace.Name.ToString(),
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            DelegateDeclarationSyntax @delegate => @delegate.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => destructor.Identifier.ValueText,
            OperatorDeclarationSyntax operatorDeclaration => $"operator {operatorDeclaration.OperatorToken.ValueText}",
            ConversionOperatorDeclarationSyntax conversion => $"operator {conversion.Type}",
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            IndexerDeclarationSyntax => "this",
            EventDeclarationSyntax @event => @event.Identifier.ValueText,
            EnumMemberDeclarationSyntax member => member.Identifier.ValueText,
            VariableDeclaratorSyntax variable => variable.Identifier.ValueText,
            _ => node.Kind().ToString()
        };

    private static DomainSymbolKind SyntaxSymbolKind(SyntaxNode node)
        => node switch
        {
            NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax => DomainSymbolKind.Namespace,
            BaseTypeDeclarationSyntax type when type is RecordDeclarationSyntax => DomainSymbolKind.Record,
            BaseTypeDeclarationSyntax type when type.Kind() == SyntaxKind.InterfaceDeclaration => DomainSymbolKind.Interface,
            BaseTypeDeclarationSyntax type when type.Kind() == SyntaxKind.StructDeclaration => DomainSymbolKind.Struct,
            BaseTypeDeclarationSyntax type when type.Kind() == SyntaxKind.EnumDeclaration => DomainSymbolKind.Enum,
            BaseTypeDeclarationSyntax => DomainSymbolKind.Class,
            BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax => DomainSymbolKind.Method,
            PropertyDeclarationSyntax or IndexerDeclarationSyntax => DomainSymbolKind.Property,
            EventDeclarationSyntax => DomainSymbolKind.Event,
            VariableDeclaratorSyntax variable when variable.Parent?.Parent is EventFieldDeclarationSyntax => DomainSymbolKind.Event,
            VariableDeclaratorSyntax => DomainSymbolKind.Field,
            _ => DomainSymbolKind.Unknown
        };

    private static string TypeParameterSuffix(TypeParameterListSyntax? typeParameters)
        => typeParameters is null || typeParameters.Parameters.Count == 0
            ? string.Empty
            : $"`{typeParameters.Parameters.Count}";

    private static string ParameterSignature(ParameterListSyntax parameterList)
        => $"|params:{string.Join(",", parameterList.Parameters.Select(parameter =>
            $"{ParameterModifiers(parameter)}:{parameter.Type?.ToString() ?? "_"}"))}";

    private static string ParameterSignature(BracketedParameterListSyntax parameterList)
        => $"|params:{string.Join(",", parameterList.Parameters.Select(parameter =>
            $"{ParameterModifiers(parameter)}:{parameter.Type?.ToString() ?? "_"}"))}";

    private static string ParameterModifiers(ParameterSyntax parameter)
        => parameter.Modifiers.Any(token => token.IsKind(SyntaxKind.RefKeyword)) ? "ref"
            : parameter.Modifiers.Any(token => token.IsKind(SyntaxKind.OutKeyword)) ? "out"
            : parameter.Modifiers.Any(token => token.IsKind(SyntaxKind.InKeyword)) ? "in"
            : parameter.Modifiers.Any(token => token.IsKind(SyntaxKind.ParamsKeyword)) ? "params"
            : string.Empty;

    private static string? FindTargetKey(
        ISymbol target,
        string identity,
        IReadOnlyDictionary<string, List<DeclaredSymbol>> declarationsByIdentity,
        IReadOnlyDictionary<string, Guid> fileIdsByPath)
    {
        if (!declarationsByIdentity.TryGetValue(identity, out var declarations))
        {
            return null;
        }

        foreach (var location in target.Locations.Where(item => item.IsInSource && item.SourceTree is not null))
        {
            var path = NormalizePath(location.SourceTree!.FilePath ?? string.Empty);
            if (fileIdsByPath.TryGetValue(path, out var sourceFileId))
            {
                var declaration = declarations.FirstOrDefault(item => item.SourceFileId == sourceFileId);
                if (declaration is not null)
                {
                    return declaration.Key;
                }
            }
        }

        return declarations[0].Key;
    }

    private static bool IsSupportedDeclaration(SyntaxNode node)
        => node is NamespaceDeclarationSyntax
            or FileScopedNamespaceDeclarationSyntax
            or BaseTypeDeclarationSyntax
            or DelegateDeclarationSyntax
            or BaseMethodDeclarationSyntax
            or LocalFunctionStatementSyntax
            or PropertyDeclarationSyntax
            or IndexerDeclarationSyntax
            or EventDeclarationSyntax
            or EnumMemberDeclarationSyntax
            || node is VariableDeclaratorSyntax variable
                && variable.Parent?.Parent is FieldDeclarationSyntax or EventFieldDeclarationSyntax;

    private static DomainSymbolKind MapSymbolKind(ISymbol symbol)
    {
        if (symbol is INamespaceSymbol)
        {
            return DomainSymbolKind.Namespace;
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            return namedType.TypeKind switch
            {
                TypeKind.Class => namedType.IsRecord ? DomainSymbolKind.Record : DomainSymbolKind.Class,
                TypeKind.Struct => namedType.IsRecord ? DomainSymbolKind.Record : DomainSymbolKind.Struct,
                TypeKind.Interface => DomainSymbolKind.Interface,
                TypeKind.Enum => DomainSymbolKind.Enum,
                _ => DomainSymbolKind.Unknown
            };
        }

        return symbol.Kind switch
        {
            RoslynSymbolKind.Method or RoslynSymbolKind.Property or RoslynSymbolKind.Field or RoslynSymbolKind.Event
                => Enum.Parse<DomainSymbolKind>(symbol.Kind.ToString(), ignoreCase: false),
            _ => DomainSymbolKind.Unknown
        };
    }

    private static string SymbolIdentity(ISymbol symbol)
    {
        var original = symbol is IMethodSymbol method ? method.OriginalDefinition : symbol.OriginalDefinition;
        var identity = $"{original.Kind}:{original.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
        if (original is IMethodSymbol originalMethod)
        {
            identity += $"|arity:{originalMethod.Arity}|params:{string.Join(",", originalMethod.Parameters.Select(parameter => $"{parameter.RefKind}:{parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"))}";
        }
        else if (original is IPropertySymbol originalProperty && originalProperty.Parameters.Length > 0)
        {
            identity += $"|params:{string.Join(",", originalProperty.Parameters.Select(parameter => $"{parameter.RefKind}:{parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"))}";
        }

        return identity;
    }

    private static string CreateSymbolKey(Guid scanId, Guid sourceFileId, string identity)
    {
        var value = $"scan:{scanId:N}/file:{sourceFileId:N}/{identity}";
        if (value.Length <= 2000)
        {
            return value;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        return $"scan:{scanId:N}/file:{sourceFileId:N}/sha256:{hash}";
    }

    private static IEnumerable<MetadataReference> PlatformReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var paths = trustedAssemblies is null
            ? new[]
            {
                typeof(object).Assembly.Location,
                typeof(Console).Assembly.Location,
                typeof(Enumerable).Assembly.Location,
                typeof(System.Runtime.GCSettings).Assembly.Location
            }
            : trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in paths
            .Where(File.Exists)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            MetadataReference? reference = null;
            try
            {
                reference = MetadataReference.CreateFromFile(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
            {
                // A single unusable runtime assembly must not prevent source-only analysis.
            }

            if (reference is not null)
            {
                yield return reference;
            }
        }
    }

    private static string NormalizePath(string path)
        => PathNormalization.Normalize(path);

    private static int? StartLine(SyntaxNode node)
        => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static int? EndLine(SyntaxNode node)
        => node.GetLocation().GetLineSpan().EndLinePosition.Line + 1;

    private static int? StartLine(SyntaxTrivia trivia)
        => trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static int CompareDependencies(
        ForgeDesk.Application.AnalysisDependencyResult left,
        ForgeDesk.Application.AnalysisDependencyResult right)
        => StringComparer.Ordinal.Compare(
            $"{left.SourceSymbolKey}\u001f{left.TargetSymbolKey}\u001f{left.RelationshipType}",
            $"{right.SourceSymbolKey}\u001f{right.TargetSymbolKey}\u001f{right.RelationshipType}");

    private static int CompareFindings(
        ForgeDesk.Application.AnalysisFindingResult left,
        ForgeDesk.Application.AnalysisFindingResult right)
        => StringComparer.Ordinal.Compare(
            $"{left.SourceFileId}\u001f{left.StartLine}\u001f{left.RuleId}\u001f{left.Message}",
            $"{right.SourceFileId}\u001f{right.StartLine}\u001f{right.RuleId}\u001f{right.Message}");

    private sealed record FileContext(
        ApplicationAnalysisFileInput Input,
        SyntaxTree Tree,
        SemanticModel? Model)
    {
        public string Path => NormalizePath(Input.NormalizedPath);
    }

    private sealed record DeclaredSymbol(
        string Key,
        string Identity,
        Guid SourceFileId,
        string Path,
        string Name,
        DomainSymbolKind Kind,
        ISymbol? Symbol,
        SyntaxNode Node);

    private sealed record WorkspaceSnapshot(
        IReadOnlyList<FileContext> Files,
        IReadOnlyList<CSharpCompilation> Compilations,
        AnalysisBindingMode BindingMode,
        string? BindingMessage);

    private readonly record struct DependencyIdentity(string Source, string? Target, string Relationship);

    private sealed class DependencyIdentityComparer : IEqualityComparer<DependencyIdentity>
    {
        public static DependencyIdentityComparer Instance { get; } = new();

        public bool Equals(DependencyIdentity x, DependencyIdentity y)
            => StringComparer.Ordinal.Equals(x.Source, y.Source)
                && StringComparer.Ordinal.Equals(x.Target, y.Target)
                && StringComparer.Ordinal.Equals(x.Relationship, y.Relationship);

        public int GetHashCode(DependencyIdentity obj)
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Source),
                obj.Target is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Target),
                StringComparer.Ordinal.GetHashCode(obj.Relationship));
    }
}
