const ROUTES = {
  projects: { label: "Projects & Onboarding", group: "Workspace" },
  explorer: { label: "Code Explorer", group: "Workspace" },
  graph: { label: "Dependency Graph", group: "Workspace" },
  health: { label: "Code Health", group: "Workspace" },
  git: { label: "Git & History", group: "Workspace" },
  reports: { label: "Reports & Export", group: "Workspace" },
  settings: { label: "Settings & Theme", group: "System" }
};

const state = {
  route: "projects",
  projects: [],
  selectedProjectId: localStorage.getItem("forgedesk-project") || "",
  selectedScanId: "",
  detail: null,
  graph: null,
  graphLoadedFor: "",
  graphQuery: { search: "", direction: "Both", symbolKind: "", relationship: "", depth: "", includeIsolated: true },
  graphViewport: null,
  graphDragging: null,
  graphSearchFocusKey: "",
  selectedNodeId: "",
  treeSearch: "",
  treeExpanded: new Set(["root"]),
  findings: [],
  findingsKey: "",
  findingFilters: { severity: "", rule: "", path: "" },
  files: [],
  filesLoadedFor: "",
  git: null,
  gitLoadedFor: "",
  comparison: null,
  comparisonKey: "",
  reportBaselineId: "",
  reportCurrentId: "",
  analysisPolling: false,
  inspector: null,
  theme: localStorage.getItem("forgedesk-theme") || "dark",
  sidebarCollapsed: localStorage.getItem("forgedesk-sidebar") === "collapsed",
  debounceTimer: null,
  toastTimer: null
};

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));
const appView = () => $("#app-view");

document.addEventListener("DOMContentLoaded", init);

async function init() {
  applyTheme(state.theme);
  applySidebarState();
  wireGlobalEvents();
  state.route = routeFromHash();
  if (!location.hash) history.replaceState(null, "", "#projects");
  await loadProjects();
  await renderRoute();
}

function wireGlobalEvents() {
  window.addEventListener("hashchange", async () => {
    state.route = routeFromHash();
    closeInspector();
    await renderRoute();
  });

  $("#refresh-all").addEventListener("click", refreshCurrentView);
  $("#project-selector").addEventListener("change", (event) => selectProject(event.target.value));
  $("#open-onboarding").addEventListener("click", openOnboarding);
  $("#sidebar-collapse").addEventListener("click", toggleSidebar);
  $("#mobile-menu").addEventListener("click", () => $("#sidebar").classList.toggle("mobile-open"));
  $("#header-theme").addEventListener("click", toggleTheme);
  $("#close-inspector").addEventListener("click", closeInspector);
  $("#drawer-backdrop").addEventListener("click", closeInspector);
  $("#sidebar-help").addEventListener("click", () => showToast("ForgeDesk Web reuses the existing C# application, analysis, graph, Git, and infrastructure services."));

  $("#app-view").addEventListener("click", handleActionClick);
  $("#app-view").addEventListener("input", handleViewInput);
  $("#app-view").addEventListener("change", handleViewChange);
  $("#inspector-content").addEventListener("click", handleActionClick);
  $("#modal-root").addEventListener("click", handleModalClick);
  $("#modal-root").addEventListener("submit", handleModalSubmit);
}

function routeFromHash() {
  const candidate = (location.hash || "#projects").slice(1).split("?")[0].toLowerCase();
  return ROUTES[candidate] ? candidate : "projects";
}

async function renderRoute() {
  const route = state.route;
  updateNavigation(route);
  updateHeader(route);
  appView().innerHTML = loadingView();

  try {
    let markup;
    switch (route) {
      case "projects":
        markup = await renderProjectsView();
        break;
      case "explorer":
        markup = await renderExplorerView();
        break;
      case "graph":
        markup = await renderGraphView();
        break;
      case "health":
        markup = await renderHealthView();
        break;
      case "git":
        markup = await renderGitView();
        break;
      case "reports":
        markup = await renderReportsView();
        break;
      case "settings":
        markup = renderSettingsView();
        break;
      default:
        markup = await renderProjectsView();
        break;
    }
    if (state.route === route) {
      appView().innerHTML = markup;
      afterViewRender(route);
    }
  } catch (error) {
    appView().innerHTML = errorView(error.message);
    showToast(error.message, true);
  }
}

function afterViewRender(route) {
  if (route === "graph" && state.graph) {
    decorateGraphView();
    renderGraphSvg();
    attachGraphInteractions();
    focusGraphSearchResult();
  }
}

async function loadProjects() {
  try {
    state.projects = await fetchJson("/api/projects");
    $("#connection-status").innerHTML = '<span class="status-dot"></span>Connected';
    $("#project-selector").innerHTML = '<option value="">Select a project</option>' + state.projects.map((project) =>
      `<option value="${escapeAttr(project.id)}">${escapeHtml(project.name)}</option>`).join("");

    if (!state.projects.length) {
      state.selectedProjectId = "";
      state.selectedScanId = "";
      state.detail = null;
      updateProjectHeader();
      return;
    }

    if (!state.projects.some((project) => project.id === state.selectedProjectId)) {
      state.selectedProjectId = state.projects[0].id;
    }
    localStorage.setItem("forgedesk-project", state.selectedProjectId);
    $("#project-selector").value = state.selectedProjectId;
    await loadProjectDetail(state.selectedProjectId);
  } catch (error) {
    $("#connection-status").innerHTML = '<span class="status-dot" style="background: var(--red)"></span>Offline';
    throw error;
  }
}

async function selectProject(projectId) {
  if (!projectId) return;
  state.selectedProjectId = projectId;
  state.selectedNodeId = "";
  state.graph = null;
  state.graphLoadedFor = "";
  state.graphSearchFocusKey = "";
  state.findings = [];
  state.findingsKey = "";
  state.files = [];
  state.filesLoadedFor = "";
  state.git = null;
  state.gitLoadedFor = "";
  state.reportBaselineId = "";
  state.reportCurrentId = "";
  state.comparison = null;
  state.comparisonKey = "";
  localStorage.setItem("forgedesk-project", projectId);
  $("#project-selector").value = projectId;
  closeInspector();
  await loadProjectDetail(projectId);
  await renderRoute();
}

async function loadProjectDetail(projectId) {
  state.detail = await fetchJson(`/api/projects/${projectId}`);
  state.selectedScanId = state.detail.scans?.[0]?.id || "";
  updateProjectHeader();
}

function updateProjectHeader() {
  const project = selectedProject();
  const scan = selectedScan();
  $("#header-project-name").textContent = project?.name || "No project selected";
  const badge = $("#header-scan-badge");
  if (!scan) {
    badge.className = "status-badge neutral";
    badge.textContent = "No scan";
    return;
  }
  const status = scan.analysis?.status || scan.status;
  badge.className = `status-badge ${statusTone(status)}`;
  badge.textContent = scan.analysis?.status && scan.analysis.status !== "NotStarted"
    ? `Analysis ${analysisStatusLabel(scan.analysis.status)}`
    : `Discovery ${scan.status}`;
}

function updateNavigation(route) {
  $$(".nav-item").forEach((item) => item.classList.toggle("active", item.dataset.route === route));
  const metadata = ROUTES[route];
  $("#breadcrumbs").innerHTML = `<span>ForgeDesk</span><b>/</b><strong>${escapeHtml(metadata.label)}</strong>`;
}

function updateHeader(route) {
  updateProjectHeader();
  $("#header-theme").textContent = state.theme === "dark" ? "☾" : "☀";
  $("#header-theme").title = state.theme === "dark" ? "Switch to light theme" : "Switch to dark theme";
  document.title = `${ROUTES[route]?.label || "ForgeDesk"} · ForgeDesk Web`;
}

async function refreshCurrentView() {
  try {
    await loadProjects();
    await renderRoute();
    showToast("Workspace refreshed.");
  } catch (error) {
    showToast(error.message, true);
  }
}

function selectedProject() {
  return state.detail?.project || state.projects.find((project) => project.id === state.selectedProjectId) || null;
}

function selectedScan() {
  return state.detail?.scans?.find((scan) => scan.id === state.selectedScanId) || state.detail?.scans?.[0] || null;
}

async function ensureAnalysisData(projectId = state.selectedProjectId, scanId = state.selectedScanId) {
  if (!projectId || !scanId) return null;
  const scan = await fetchJson(`/api/projects/${projectId}/scans/${scanId}/analysis`);
  if (projectId === state.selectedProjectId && scanId === state.selectedScanId) {
    mergeScan(scan);
    updateProjectHeader();
  }
  return scan;
}

async function ensureFiles() {
  if (!state.selectedProjectId || !state.selectedScanId) return [];
  if (state.filesLoadedFor === state.selectedScanId) return state.files;
  state.files = await fetchJson(`/api/projects/${state.selectedProjectId}/scans/${state.selectedScanId}/files`);
  state.filesLoadedFor = state.selectedScanId;
  return state.files;
}

async function ensureFindings(force = false) {
  if (!state.selectedProjectId || !state.selectedScanId) return [];
  const key = `${state.selectedScanId}|${state.findingFilters.severity}|${state.findingFilters.rule}|${state.findingFilters.path}`;
  if (!force && state.findingsKey === key) return state.findings;
  const params = new URLSearchParams();
  for (const [keyName, value] of Object.entries(state.findingFilters)) if (value) params.set(keyName, value);
  state.findings = await fetchJson(`/api/projects/${state.selectedProjectId}/scans/${state.selectedScanId}/findings?${params}`);
  state.findingsKey = key;
  return state.findings;
}

async function ensureGraph(force = false) {
  if (!state.selectedProjectId || !state.selectedScanId) return null;
  const query = state.graphQuery;
  const key = `${state.selectedScanId}|${query.search}|${query.direction}|${query.symbolKind}|${query.relationship}|${query.depth}|${query.includeIsolated}|${state.selectedNodeId}`;
  if (!force && state.graph && state.graphLoadedFor === key) return state.graph;
  const params = new URLSearchParams();
  if (query.search) params.set("search", query.search);
  if (query.direction) params.set("direction", query.direction);
  if (query.symbolKind) params.set("symbolKind", query.symbolKind);
  if (query.relationship) params.set("relationship", query.relationship);
  if (query.depth) params.set("depth", query.depth);
  if (!query.includeIsolated) params.set("includeIsolated", "false");
  if (state.selectedNodeId) params.set("selected", state.selectedNodeId);
  state.graph = await fetchJson(`/api/projects/${state.selectedProjectId}/scans/${state.selectedScanId}/graph?${params}`);
  state.graphLoadedFor = key;
  if (!state.graphViewport) fitGraphViewport(state.graph);
  return state.graph;
}

async function ensureGit(force = false) {
  if (!state.selectedProjectId) return null;
  if (!force && state.git && state.gitLoadedFor === state.selectedProjectId) return state.git;
  state.git = await fetchJson(`/api/projects/${state.selectedProjectId}/git`);
  state.gitLoadedFor = state.selectedProjectId;
  return state.git;
}

async function ensureComparison(baselineId, currentId) {
  if (!state.selectedProjectId || !baselineId || !currentId || baselineId === currentId) return null;
  const key = `${state.selectedProjectId}|${baselineId}|${currentId}`;
  if (state.comparison && state.comparisonKey === key) return state.comparison;
  state.comparison = await fetchJson(`/api/projects/${state.selectedProjectId}/compare?baselineScanId=${encodeURIComponent(baselineId)}&currentScanId=${encodeURIComponent(currentId)}`);
  state.comparisonKey = key;
  return state.comparison;
}

function mergeScan(scan) {
  if (!state.detail || !scan) return;
  const index = state.detail.scans.findIndex((item) => item.id === scan.id);
  if (index >= 0) state.detail.scans[index] = scan;
  else state.detail.scans.unshift(scan);
}

async function startAnalysis(scanId) {
  if (!state.selectedProjectId || state.analysisPolling) return;
  const projectId = state.selectedProjectId;
  state.selectedScanId = scanId;
  try {
    await fetchJson(`/api/projects/${projectId}/scans/${scanId}/analysis`, { method: "POST" });
    state.analysisPolling = true;
    showToast("Analysis queued. Progress will update in this workspace.");
    const queuedScan = await ensureAnalysisData(projectId, scanId);
    if (state.route === "projects" && projectId === state.selectedProjectId && scanId === state.selectedScanId) patchAnalysisProgress(queuedScan);
    for (let attempt = 0; attempt < 600; attempt += 1) {
      const scan = await ensureAnalysisData(projectId, scanId);
      if (state.route === "projects" && projectId === state.selectedProjectId && scanId === state.selectedScanId) patchAnalysisProgress(scan);
      if (!scan || !["Queued", "Running"].includes(scan.analysis?.status)) {
        state.analysisPolling = false;
        state.findingsKey = "";
        state.graphLoadedFor = "";
        if (scan?.analysis?.status === "Completed") showToast("Analysis completed.");
        else if (scan?.analysis?.status === "Failed") showToast(scan.analysis.errorMessage || "Analysis failed.", true);
        if (projectId === state.selectedProjectId && scanId === state.selectedScanId) await renderRoute();
        return;
      }
      await sleep(1000);
    }
    showToast("Analysis is still running. Refresh to see the persisted result.");
  } catch (error) {
    showToast(error.message, true);
  } finally {
    state.analysisPolling = false;
  }
}

function openOnboarding() {
  $("#modal-root").innerHTML = `<div class="modal-backdrop" data-action="close-modal"><section class="modal" role="dialog" aria-modal="true" aria-labelledby="onboard-title" data-modal-shell>
    <div class="modal-header"><div><span class="eyebrow">Workspace setup</span><h2 id="onboard-title">Onboard a local project</h2></div><button class="icon-button" type="button" data-action="close-modal" aria-label="Close dialog">×</button></div>
    <form id="onboard-modal-form"><div class="modal-body"><div class="form-grid">
      <div class="field"><label for="onboard-name">Project name</label><input id="onboard-name" name="name" maxlength="200" required placeholder="My .NET workspace"></div>
      <div class="field"><label for="onboard-root-path">Local folder path</label><input id="onboard-root-path" name="rootPath" required placeholder="C:\\Projects\\MyApp or /workspace/MyApp"><span class="field-help">The path is resolved by the machine running ForgeDesk.Web.</span></div>
      <div class="field"><label for="onboard-description">Description <span class="muted">optional</span></label><textarea id="onboard-description" name="description" maxlength="2000" placeholder="What are you exploring?"></textarea></div>
      <p id="onboard-status" class="form-status"></p>
    </div></div><div class="modal-footer"><button type="button" class="button button-secondary" data-action="close-modal">Cancel</button><button id="onboard-submit" class="button button-primary" type="submit">Discover project</button></div></form>
  </section></div>`;
  $("#onboard-name")?.focus();
}

async function handleModalSubmit(event) {
  if (event.target.id !== "onboard-modal-form") return;
  event.preventDefault();
  const form = event.target;
  const submit = $("#onboard-submit", form);
  const status = $("#onboard-status", form);
  submit.disabled = true;
  status.textContent = "Discovering files and saving the scan…";
  status.className = "form-status";
  try {
    const result = await fetchJson("/api/projects/onboard", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        name: $("#onboard-name", form).value.trim(),
        rootPath: $("#onboard-root-path", form).value.trim(),
        description: $("#onboard-description", form).value.trim() || null
      })
    });
    state.selectedProjectId = result.project.id;
    $("#modal-root").innerHTML = "";
    await loadProjects();
    await renderRoute();
    showToast(`Onboarded ${result.project.name}.`);
  } catch (error) {
    status.className = "form-status error";
    status.textContent = error.message;
    submit.disabled = false;
  }
}

function handleModalClick(event) {
  const action = event.target.closest("[data-action]")?.dataset.action;
  if (action === "close-modal" && (event.target === event.currentTarget || event.target.closest("[data-modal-shell]") === null || event.target.closest("button[data-action='close-modal']"))) {
    $("#modal-root").innerHTML = "";
  }
}

async function renderProjectsView() {
  if (!state.selectedProjectId || !state.detail) {
    return `<div class="page-view"><div class="page-header"><div><span class="eyebrow">Workspace inventory</span><h1>Projects &amp; onboarding</h1><p>Register a local solution or source folder to begin discovery, analysis, graph exploration, and reporting.</p></div><div class="page-header-actions"><button class="button button-primary" data-action="open-onboarding">＋ Add project</button></div></div>${emptyProjectPanel()}</div>`;
  }

  await ensureAnalysisData();
  const project = selectedProject();
  const scan = selectedScan();
  const analysis = scan?.analysis || {};
  return `<div class="page-view">
    <div class="page-header"><div><span class="eyebrow">Workspace inventory</span><h1>Projects &amp; onboarding</h1><p>Choose a registered workspace, inspect its persisted scans, or onboard another local folder.</p></div><div class="page-header-actions"><button class="button button-secondary" data-action="refresh-projects">↻ Refresh</button><button class="button button-primary" data-action="open-onboarding">＋ Add project</button></div></div>
     <div class="metric-strip" data-analysis-metrics>
       ${metricCard("Registered projects", state.projects.length, "Recent local workspaces", "metric-blue")}
       ${metricCard("Source files", scan?.filesDiscovered ?? "—", "Selected discovery scan", "metric-purple", "source-files")}
       ${metricCard("Findings", analysis.findingCount ?? "—", "Selected analysis run", "metric-yellow", "findings")}
       ${metricCard("Analysis", analysis.status || "Not started", analysis.phase || "Ready for a run", "metric-green", "analysis")}
    </div>
    <section class="project-hero"><div><span class="eyebrow">Active workspace</span><h2>${escapeHtml(project.name)}</h2><p>${escapeHtml(project.description || "Local .NET project intelligence workspace")}</p><div class="project-path mono" title="${escapeAttr(project.rootPath)}">${escapeHtml(project.rootPath)}</div><div class="project-hero-actions"><a class="button button-secondary" href="#explorer">Open Code Explorer</a><a class="button button-secondary" href="#graph">Open Dependency Graph</a><button class="button button-primary" data-action="open-onboarding">＋ Onboard another</button></div></div><div class="scan-summary">${summaryItem("Project type", project.projectType)}${summaryItem("Discovery", scan?.status || "—")}${summaryItem("Last scanned", formatDate(project.lastScannedAtUtc))}${summaryItem("Binding", analysis.bindingMode || "—")}</div></section>
    <div class="grid grid-2"><section class="panel"><div class="panel-header"><div><h2>Recent projects</h2><p>Select a workspace to make it active.</p></div><span class="badge">${state.projects.length}</span></div><div class="project-list">${state.projects.map(renderProjectCard).join("") || emptyState("No projects have been onboarded yet.")}</div></section><section class="panel"><div class="panel-header"><div><h2>Scan lifecycle</h2><p>Discovery and Roslyn analysis runs for the active project.</p></div><span class="badge badge-blue">Persisted</span></div><div class="panel-body"><div class="scan-list">${(state.detail.scans || []).map(renderScanRow).join("") || emptyState("No discovery scans are available for this project.")}</div></div></section></div>
    <p class="footer-note">Folder paths are resolved by the machine running ForgeDesk.Web. The browser never reads source files directly.</p>
  </div>`;
}

async function renderExplorerView() {
  if (!state.selectedProjectId || !state.detail) return noSelectionView("Code Explorer", "Select a project before opening the hierarchy tree.");
  const graph = await ensureGraph();
  if (!graph || !graph.nodes?.length) return noAnalysisView("Code Explorer", "Run analysis for the selected scan to build a namespace and symbol hierarchy.");
  const selected = graph.nodes.find((node) => node.id === state.selectedNodeId) || null;
  const tree = buildTreeModel(graph.nodes);
  return `<div class="page-view"><div class="page-header"><div><span class="eyebrow">Symbol navigation</span><h1>Code Explorer</h1><p>Navigate from the assembly root through namespaces, types, and members. Select any symbol to inspect its signature and connections.</p></div><div class="page-header-actions"><span class="status-badge ${statusTone(selectedScan()?.analysis?.status)}">${escapeHtml(selectedScan()?.analysis?.status || "No analysis")}</span><a class="button button-secondary" href="#graph">Open graph</a></div></div><div class="explorer-layout"><section class="panel tree-panel"><div class="panel-header"><div><h2>Hierarchy</h2><p>${graph.nodes.length} persisted symbols</p></div><span class="badge">${escapeHtml(selectedScan()?.id?.slice(0, 8) || "—")}</span></div><div class="tree-search"><input id="tree-search" data-action="tree-search" value="${escapeAttr(state.treeSearch)}" placeholder="Filter namespaces, types, methods…" aria-label="Filter hierarchy"></div><div class="tree-scroll">${renderTreeHtml(tree)}</div></section><section class="panel explorer-detail">${selected ? renderEntityInline(selected, graph) : `<div class="empty-state"><div><strong>Select a symbol from the hierarchy</strong><p class="subtle">Details, source location, and incoming/outgoing connections will appear here.</p></div></div>`}</section></div></div>`;
}

async function renderGraphView() {
  if (!state.selectedProjectId || !state.detail) return noSelectionView("Dependency Graph", "Select a project before opening the dependency graph.");
  const graph = await ensureGraph();
  const selected = graph?.nodes?.find((node) => node.id === state.selectedNodeId) || null;
  return `<div class="page-view"><div class="page-header"><div><span class="eyebrow">Architecture graph</span><h1>Dependency Graph</h1><p>Explore directed relationships with pan, zoom, selection focus, symbol filters, and a live connection inspector.</p></div><div class="page-header-actions"><span class="status-badge ${statusTone(graph?.state)}">${escapeHtml(graph?.state || "No graph")}</span><button class="button button-secondary" data-action="reload-graph">↻ Refresh graph</button></div></div><div class="graph-layout"><section class="panel graph-main"><div class="graph-toolbar"><div class="field"><label for="graph-search">Search symbols</label><input id="graph-search" data-action="graph-search" value="${escapeAttr(state.graphQuery.search)}" placeholder="Name, namespace, or path"></div><div class="field"><label for="graph-kind">Kind</label><select id="graph-kind" data-action="graph-kind"><option value="">All kinds</option>${["Class", "Interface", "Struct", "Enum", "Record", "Method", "Property", "Event"].map((kind) => `<option ${state.graphQuery.symbolKind === kind ? "selected" : ""}>${kind}</option>`).join("")}</select></div><div class="field"><label for="graph-relationship">Relationship</label><select id="graph-relationship" data-action="graph-relationship"><option value="">All relationships</option>${["Calls", "TypeReference", "Inherits", "Implements"].map((kind) => `<option ${state.graphQuery.relationship === kind ? "selected" : ""}>${kind}</option>`).join("")}</select></div><div class="field"><label for="graph-direction">Direction</label><select id="graph-direction" data-action="graph-direction"><option ${state.graphQuery.direction === "Both" ? "selected" : ""}>Both</option><option ${state.graphQuery.direction === "Incoming" ? "selected" : ""}>Incoming</option><option ${state.graphQuery.direction === "Outgoing" ? "selected" : ""}>Outgoing</option></select></div><div class="toolbar-actions"><button class="button button-secondary" data-action="apply-graph-filters">Apply</button></div></div><div class="graph-canvas-wrap"><svg id="graph-canvas" role="img" aria-label="Interactive dependency graph"></svg><div class="graph-empty" style="display:${graph?.nodes?.length ? "none" : "grid"}">${escapeHtml(graph?.statusMessage || "No dependency data is available for this scan.")}</div><div class="graph-controls"><button data-action="graph-zoom-out" title="Zoom out">−</button><button data-action="graph-fit" title="Fit graph">Fit</button><button data-action="graph-zoom-in" title="Zoom in">＋</button><button data-action="graph-reset" title="Reset graph">Reset</button></div></div></section><aside class="panel graph-side"><div class="panel-header"><div><h2>Connections</h2><p>Focus a symbol to inspect its relationships.</p></div><button class="icon-button" data-action="close-inspector" title="Close drawer">×</button></div>${selected ? renderConnectionPanel(selected, graph) : `<div class="empty-state">Click a node to open its connection inspector.</div>`}<div class="graph-stats">${graphStat("Nodes", graph?.nodes?.length || 0)}${graphStat("Edges", graph?.edges?.length || 0)}${graphStat("State", graph?.state || "—")}${graphStat("Selected", selected ? "1" : "0")}</div><div class="legend"><div class="legend-row"><i class="legend-line"></i> persisted relationship</div><div class="legend-row"><i class="legend-line highlight"></i> selected / highlighted</div></div></aside></div></div>`;
}

async function renderHealthView() {
  if (!state.selectedProjectId || !state.detail) return noSelectionView("Code Health", "Select a project before opening diagnostics.");
  const scan = await ensureAnalysisData();
  const findings = await ensureFindings();
  return `<div class="page-view"><div class="page-header"><div><span class="eyebrow">Deterministic diagnostics</span><h1>Code Health</h1><p>Review persisted findings by severity, rule, and source path. Select an issue to open the source-aware inspector.</p></div><div class="page-header-actions"><span class="status-badge ${statusTone(scan?.analysis?.status)}">${escapeHtml(scan?.analysis?.status || "Not started")}</span><button class="button button-secondary" data-action="reload-findings">↻ Refresh</button></div></div><div class="panel"><div class="toolbar"><div class="field small"><label for="health-severity">Severity</label><select id="health-severity" data-action="health-severity"><option value="">All severities</option>${["Critical", "Error", "Warning", "Info"].map((item) => `<option ${state.findingFilters.severity === item ? "selected" : ""}>${item}</option>`).join("")}</select></div><div class="field small"><label for="health-rule">Rule</label><input id="health-rule" data-action="health-rule" value="${escapeAttr(state.findingFilters.rule)}" placeholder="FD001"></div><div class="field"><label for="health-path">Relative path</label><input id="health-path" data-action="health-path" value="${escapeAttr(state.findingFilters.path)}" placeholder="src/ or folder/"></div><div class="toolbar-actions"><button class="button button-secondary" data-action="apply-health-filters">Apply filters</button><button class="button button-quiet" data-action="clear-health-filters">Clear</button></div></div><div class="rule-categories"><button class="filter-chip ${state.findingFilters.rule === "FD001" ? "active" : ""}" data-action="rule-chip" data-rule="FD001">FD001 TODO / FIXME</button><button class="filter-chip ${state.findingFilters.rule === "FD002" ? "active" : ""}" data-action="rule-chip" data-rule="FD002">FD002 Long method</button><button class="filter-chip ${state.findingFilters.rule === "FD003" ? "active" : ""}" data-action="rule-chip" data-rule="FD003">FD003 Parameters</button><button class="filter-chip ${state.findingFilters.rule === "FD004" ? "active" : ""}" data-action="rule-chip" data-rule="FD004">FD004 Empty catch</button></div><div class="panel-header"><div><h2>${findings.length} finding${findings.length === 1 ? "" : "s"}</h2><p>Click a row to inspect the rule, source lines, and related symbol.</p></div><span class="badge badge-yellow">${escapeHtml(scan?.analysis?.bindingMode || "No binding metadata")}</span></div><div class="health-list">${findings.map(renderFindingRow).join("") || emptyState("No findings match the current filters.")}</div></div></div>`;
}

async function renderGitView() {
  if (!state.selectedProjectId || !state.detail) return noSelectionView("Git & History", "Select a project before opening repository history.");
  const git = await ensureGit();
  const stateTone = git?.state === "Clean" ? "green" : git?.state === "Dirty" ? "yellow" : "neutral";
  return `<div class="page-view"><div class="page-header"><div><span class="eyebrow">Read-only repository context</span><h1>Git &amp; History</h1><p>Inspect branch metadata, working-tree changes, and bounded recent commits without staging or mutating files.</p></div><div class="page-header-actions"><span class="status-badge ${stateTone}">${escapeHtml(git?.state || "Not loaded")}</span><button class="button button-secondary" data-action="reload-git">↻ Refresh Git</button></div></div><div class="git-grid"><section class="panel"><div class="git-status-hero"><div class="git-status-icon">⑂</div><div><h2>${escapeHtml(git?.branch?.name || (git?.branch?.isDetached ? "Detached HEAD" : "No repository"))}</h2><p title="${escapeAttr(git?.repositoryRootPath || "")}">${escapeHtml(git?.repositoryRootPath || git?.errorMessage || "No .git metadata was found for this project.")}</p></div></div><div class="git-facts">${gitFact("HEAD", git?.headCommit?.id ? git.headCommit.id.slice(0, 10) : "—")}${gitFact("Changes", git?.changes?.length ?? 0)}${gitFact("Recent commits", git?.recentCommits?.length ?? 0)}</div><div class="panel-header"><div><h2>Working tree</h2><p>Changed and untracked paths from the local repository.</p></div><span class="badge">${git?.changesTruncated ? "Truncated" : "Bounded"}</span></div><div class="change-list">${(git?.changes || []).map((change) => `<div class="change-row"><span class="change-kind">${escapeHtml(change.statusCode || "—")}</span><span class="change-path" title="${escapeAttr(change.relativePath)}">${escapeHtml(change.relativePath)}</span><span class="badge">${escapeHtml(change.kind)}</span></div>`).join("") || emptyState("Working tree is clean or no repository was found.")}</div></section><section class="panel"><div class="panel-header"><div><h2>Recent commits</h2><p>Latest bounded history from the nearest repository.</p></div><span class="badge badge-purple">${git?.historyTruncated ? "Truncated" : "Read-only"}</span></div><div class="commit-list">${(git?.recentCommits || []).map((commit) => `<div class="commit-row"><span class="commit-sha">${escapeHtml(commit.id?.slice(0, 8) || "—")}</span><div class="commit-copy"><strong title="${escapeAttr(commit.message)}">${escapeHtml(commit.message || "No commit message")}</strong><span>${escapeHtml(commit.authorName || "Unknown author")} · ${formatDate(commit.authorDateUtc)}</span></div></div>`).join("") || emptyState("No recent commits are available.")}</div></section></div></div>`;
}

async function renderReportsView() {
  if (!state.selectedProjectId || !state.detail) return noSelectionView("Reports & Export", "Select a project before opening report tools.");
  const scans = state.detail.scans || [];
  const completedScans = scans.filter((scan) => scan.analysis?.status === "Completed");
  const currentId = state.reportCurrentId || state.selectedScanId || completedScans[0]?.id || "";
  const baselineId = state.reportBaselineId || completedScans.find((scan) => scan.id !== currentId)?.id || "";
  state.reportCurrentId = currentId;
  state.reportBaselineId = baselineId;
  const current = scans.find((scan) => scan.id === currentId);
  const comparison = baselineId && currentId && baselineId !== currentId ? await ensureComparison(baselineId, currentId) : null;
  return `<div class="page-view"><div class="page-header"><div><span class="eyebrow">Safe analysis exports</span><h1>Reports &amp; Export</h1><p>Compare persisted analysis runs and download redacted JSON, self-contained HTML, or findings CSV exports.</p></div><div class="page-header-actions"><span class="status-badge purple">Paths redacted</span></div></div><div class="reports-grid"><section class="panel"><div class="panel-header"><div><h2>Run comparison</h2><p>Compare fingerprints across two scans from the active project.</p></div><span class="badge">${completedScans.length} completed</span></div><div class="comparison-controls"><div class="field"><label for="report-baseline">Baseline run</label><select id="report-baseline" data-action="report-baseline"><option value="">Select baseline</option>${completedScans.map((scan) => `<option value="${scan.id}" ${baselineId === scan.id ? "selected" : ""}>${formatDate(scan.queuedAtUtc)} · ${scan.id.slice(0, 8)}</option>`).join("")}</select></div><div class="field"><label for="report-current">Current run</label><select id="report-current" data-action="report-current"><option value="">Select current</option>${completedScans.map((scan) => `<option value="${scan.id}" ${currentId === scan.id ? "selected" : ""}>${formatDate(scan.queuedAtUtc)} · ${scan.id.slice(0, 8)}</option>`).join("")}</select></div><button class="button button-primary" data-action="compare-runs">Compare</button></div>${comparison ? renderComparison(comparison) : emptyState(completedScans.length > 1 ? "Choose two different completed runs to compare." : "At least two completed analysis runs are required for comparison.")}</section><section class="panel"><div class="panel-header"><div><h2>Download selected run</h2><p>${current ? `Run ${current.id.slice(0, 8)} · ${current.analysis?.findingCount ?? 0} findings` : "Select a completed run"}</p></div><span class="badge badge-blue">Export</span></div><div class="export-list">${reportLink("JSON", "Structured machine-readable report", currentId, "json")}${reportLink("HTML", "Standalone browser report", currentId, "html")}${reportLink("CSV", "Finding rows for spreadsheets", currentId, "csv")}</div></section></div></div>`;
}

function renderSettingsView() {
  return `<div class="page-view"><div class="page-header"><div><span class="eyebrow">Application preferences</span><h1>Settings &amp; Theme</h1><p>Personalize the browser workspace without changing the shared desktop or server configuration.</p></div></div><div class="settings-grid"><section class="panel"><div class="panel-header"><div><h2>Appearance</h2><p>Theme is stored in this browser only.</p></div><span class="badge">Local preference</span></div><div class="theme-card"><div><h2>${state.theme === "dark" ? "Dark studio" : "Light studio"}</h2><p>Switch instantly between the GitHub-style dark palette and a bright workspace.</p></div><label class="theme-switch" title="Toggle light/dark theme"><input type="checkbox" data-action="theme-toggle" ${state.theme === "light" ? "checked" : ""}><span></span></label></div></section><section class="panel"><div class="panel-header"><div><h2>Keyboard shortcuts</h2><p>Small navigation helpers for the web workspace.</p></div></div><div class="shortcut-list">${shortcutRow("Open project switcher", "Click active project")}${shortcutRow("Close inspector", "Escape")}${shortcutRow("Switch view", "Sidebar links")}${shortcutRow("Reset graph viewport", "Graph → Reset")}</div></section></div><section class="panel" style="margin-top:14px"><div class="panel-header"><div><h2>Runtime</h2><p>ForgeDesk Web keeps the existing C# application services as the source of truth.</p></div></div><div class="panel-body"><div class="detail-grid"><div class="detail-item"><span>Host</span><strong>ASP.NET Core .NET 8</strong></div><div class="detail-item"><span>Data</span><strong>Shared SQLite infrastructure</strong></div><div class="detail-item"><span>Analysis</span><strong>Roslyn + background worker</strong></div><div class="detail-item"><span>Safety</span><strong>Read-only Git and bounded source preview</strong></div></div></div></section></div>`;
}

function renderProjectCard(project) {
  const latest = project.latestScan;
  const status = latest?.analysis?.status || latest?.status || "Unscanned";
  return `<button class="project-card ${project.id === state.selectedProjectId ? "selected" : ""}" data-action="select-project" data-project-id="${escapeAttr(project.id)}"><div class="project-card-main"><div class="project-card-title"><span>${escapeHtml(project.name)}</span><span class="status-badge ${statusTone(status)}">${analysisStatusBadge(status)}</span></div><div class="project-card-path" title="${escapeAttr(project.rootPath)}">${escapeHtml(project.rootPath)}</div><div class="project-card-meta"><span class="badge">${escapeHtml(project.projectType)}</span>${latest ? `<span class="badge">${latest.filesDiscovered} files</span>` : ""}</div></div><div class="project-card-side"><small>${formatDate(project.lastOpenedAtUtc || project.updatedAtUtc)}</small><span>Open →</span></div></button>`;
}

function patchAnalysisProgress(scan) {
  if (!scan || state.route !== "projects") return;
  const row = $$('[data-scan-id]').find((item) => item.dataset.scanId === scan.id);
  const analysis = scan.analysis || {};
  if (!row) return;

  const status = analysis.status || "NotStarted";
  const statusBadge = $(`[data-scan-field="status"]`, row);
  if (statusBadge) {
    statusBadge.className = `status-badge ${statusTone(status)}`;
    statusBadge.innerHTML = analysisStatusBadge(status);
  }
  const runButton = $("[data-action=\"run-analysis\"]", row);
  if (runButton && ["Queued", "Running"].includes(status)) {
    runButton.disabled = true;
    runButton.textContent = status === "Queued" ? "Queued…" : "Running…";
  }

  const discovery = $(`[data-scan-field="discovery"]`, row);
  if (discovery) discovery.textContent = `Discovery ${scan.status} · ${scan.filesProcessed}/${scan.filesDiscovered} files`;
  const phase = $(`[data-scan-metric="phase"] strong`, row);
  const files = $(`[data-scan-metric="files"] strong`, row);
  const symbols = $(`[data-scan-metric="symbols"] strong`, row);
  const edges = $(`[data-scan-metric="edges"] strong`, row);
  const findings = $(`[data-scan-metric="findings"] strong`, row);
  if (phase) phase.textContent = analysis.phase || "Idle";
  if (files) files.textContent = `${analysis.filesProcessed ?? 0}/${analysis.filesTotal ?? scan.filesDiscovered}`;
  if (symbols) symbols.textContent = analysis.symbolCount ?? 0;
  if (edges) edges.textContent = analysis.dependencyCount ?? 0;
  if (findings) findings.textContent = analysis.findingCount ?? 0;
  const progress = analysis.filesTotal > 0
    ? Math.min(100, Math.round((analysis.filesProcessed / analysis.filesTotal) * 100))
    : status === "Completed" ? 100 : 0;
  const progressBar = $(`[data-scan-field="progress"]`, row);
  if (progressBar) progressBar.style.width = `${progress}%`;

  const sourceFilesCard = $(`[data-metric="source-files"]`);
  const findingsCard = $(`[data-metric="findings"]`);
  const analysisCard = $(`[data-metric="analysis"]`);
  if (sourceFilesCard) $("strong", sourceFilesCard).textContent = scan.filesDiscovered ?? "—";
  if (findingsCard) $("strong", findingsCard).textContent = analysis.findingCount ?? "—";
  if (analysisCard) {
    $("strong", analysisCard).textContent = analysisStatusLabel(status);
    $("small", analysisCard).textContent = analysis.phase || "Ready for a run";
  }
}

function renderScanRow(scan) {
  const analysis = scan.analysis || {};
  const progress = analysis.filesTotal > 0 ? Math.min(100, Math.round((analysis.filesProcessed / analysis.filesTotal) * 100)) : (analysis.status === "Completed" ? 100 : 0);
  const canAnalyze = scan.status === "Completed" && !["Queued", "Running"].includes(analysis.status);
  return `<article class="scan-row ${scan.id === state.selectedScanId ? "selected" : ""}" data-scan-id="${escapeAttr(scan.id)}"><div class="scan-row-top"><div class="scan-row-title"><strong>${formatDate(scan.queuedAtUtc)} · ${escapeHtml(scan.id.slice(0, 8))}</strong><small data-scan-field="discovery">Discovery ${escapeHtml(scan.status)} · ${scan.filesProcessed}/${scan.filesDiscovered} files</small></div><div class="scan-row-actions"><span class="status-badge ${statusTone(analysis.status)}" data-scan-field="status">${analysisStatusBadge(analysis.status)}</span><button class="button button-secondary" data-action="select-scan" data-scan-id="${escapeAttr(scan.id)}">${scan.id === state.selectedScanId ? "Selected" : "Inspect"}</button>${canAnalyze ? `<button class="button button-primary" data-action="run-analysis" data-scan-id="${escapeAttr(scan.id)}">Run analysis</button>` : ""}</div></div><div class="scan-metrics">${metricPair("Phase", analysis.phase || "Idle", "phase")}${metricPair("Files", `${analysis.filesProcessed ?? 0}/${analysis.filesTotal ?? scan.filesDiscovered}`, "files")}${metricPair("Symbols", analysis.symbolCount ?? 0, "symbols")}${metricPair("Edges", analysis.dependencyCount ?? 0, "edges")}${metricPair("Findings", analysis.findingCount ?? 0, "findings")}</div><div class="progress-track"><div class="progress-bar" data-scan-field="progress" style="width:${progress}%"></div></div></article>`;
}

function buildTreeModel(nodes) {
  const root = { key: "root", label: selectedProject()?.name || "Assembly", kind: "project", icon: "A", children: [] };
  const namespaces = new Map();
  const typeNodes = nodes.filter((node) => isTypeKind(node.kind));
  const memberNodes = nodes.filter((node) => !isTypeKind(node.kind) && node.kind !== "Namespace");

  const namespaceFor = (node) => node.namespaceName || deriveNamespace(node);
  const ensureNamespace = (value) => {
    const namespace = value || "(global)";
    if (namespaces.has(namespace)) return namespaces.get(namespace);
    let parent = root;
    let current = "";
    namespace.split(".").filter(Boolean).forEach((part) => {
      current = current ? `${current}.${part}` : part;
      if (!namespaces.has(current)) {
        const next = { key: `namespace:${current}`, label: part, kind: "Namespace", icon: "N", children: [], namespace: current };
        parent.children.push(next);
        namespaces.set(current, next);
      }
      parent = namespaces.get(current);
    });
    return namespaces.get(namespace) || parent;
  };

  const typeModels = new Map();
  typeNodes.forEach((node) => {
    const parent = ensureNamespace(namespaceFor(node));
    const model = { key: `node:${node.id}`, label: node.label, kind: node.kind, icon: symbolIcon(node.kind), node, children: [] };
    parent.children.push(model);
    typeModels.set(node.id, model);
    const fq = node.fullyQualifiedName || node.symbolKey || node.label;
    typeModels.set(`fq:${fq}`, model);
    typeModels.set(`name:${node.label}`, model);
  });

  memberNodes.forEach((node) => {
    const containing = node.containingType || "";
    const parent = typeModels.get(`fq:${containing}`) || typeModels.get(`name:${containing}`) || ensureNamespace(namespaceFor(node));
    parent.children.push({ key: `node:${node.id}`, label: node.label, kind: node.kind, icon: symbolIcon(node.kind), node, children: [] });
  });

  const sortChildren = (items) => items.sort((a, b) => {
    const rank = (item) => item.kind === "Namespace" ? 0 : isTypeKind(item.kind) ? 1 : 2;
    return rank(a) - rank(b) || a.label.localeCompare(b.label);
  }).forEach((item) => sortChildren(item.children));
  sortChildren(root.children);
  return root;
}

function renderTreeHtml(node, depth = 0) {
  const matches = treeMatches(node);
  if (!matches) return "";
  const hasChildren = node.children?.length > 0;
  const expanded = state.treeExpanded.has(node.key) || depth === 0;
  const nodeId = node.node?.id || "";
  const selected = nodeId && nodeId === state.selectedNodeId;
  return `<div class="tree-node"><div class="tree-row ${selected ? "selected" : ""}" data-action="${nodeId ? "select-node" : "tree-toggle"}" data-node-id="${escapeAttr(nodeId)}" data-tree-key="${escapeAttr(node.key)}"><button class="tree-toggle" type="button" data-action="tree-toggle" data-tree-key="${escapeAttr(node.key)}">${hasChildren ? (expanded ? "⌄" : "›") : "·"}</button><span class="tree-icon ${String(node.kind).toLowerCase()}">${escapeHtml(node.icon)}</span><span class="tree-label" title="${escapeAttr(node.node?.fullyQualifiedName || node.label)}">${escapeHtml(node.label)}</span><span class="tree-kind">${escapeHtml(node.kind)}</span></div>${hasChildren && expanded ? `<div class="tree-children">${node.children.map((child) => renderTreeHtml(child, depth + 1)).join("")}</div>` : ""}</div>`;
}

function treeMatches(node) {
  const term = state.treeSearch.trim().toLowerCase();
  if (!term) return true;
  if ((node.label || "").toLowerCase().includes(term) || (node.node?.fullyQualifiedName || "").toLowerCase().includes(term)) return true;
  return node.children?.some(treeMatches) || false;
}

function renderEntityInline(node, graph) {
  return `<div class="inspector-inline"><div class="entity-heading"><div class="entity-symbol">${escapeHtml(symbolIcon(node.kind))}</div><div><span class="eyebrow">${escapeHtml(node.kind)}</span><h2>${escapeHtml(node.label)}</h2><p>${escapeHtml(node.fullyQualifiedName || node.symbolKey || node.relativePath || "Symbol")}</p></div></div><div class="detail-grid">${detailItem("Namespace", node.namespaceName || "(global)")}${detailItem("Containing type", node.containingType || "—")}${detailItem("Source file", node.relativePath || "—", true)}${detailItem("Line range", formatLines(node.startLine, node.endLine))}${detailItem("Relative line offset", relativeLineOffset(node.startLine, node.endLine))}${detailItem("Incoming XREFs", node.incomingDependencyCount || 0)}${detailItem("Outgoing XREFs", node.outgoingDependencyCount || 0)}${detailItem("Health findings", node.relatedFindingCount || 0)}</div><div class="detail-actions"><button class="button button-primary" data-action="focus-node-graph" data-node-id="${escapeAttr(node.id)}">Focus in Graph</button><button class="button button-secondary" data-action="view-source" data-node-id="${escapeAttr(node.id)}">View Source</button><button class="button button-quiet copy-button" data-action="copy-symbol" data-node-id="${escapeAttr(node.id)}">Copy symbol</button></div>${renderConnectionPanel(node, graph)}</div>`;
}

function renderConnectionPanel(node, graph) {
  const groups = connectionGroups(node, graph);
  const visible = Object.entries(groups).filter(([, items]) => items.length);
  if (!visible.length) return `<div class="connection-section"><h3>Connections</h3><div class="empty-state">No persisted connections for this symbol.</div></div>`;
  return `<div class="connection-section"><h3>Connections</h3>${visible.map(([label, items]) => `<div class="connection-section"><h3>${escapeHtml(label)}</h3><div class="connection-list">${items.map((item) => `<button class="connection-row" data-action="select-node" data-node-id="${escapeAttr(item.node.id)}"><span class="connection-type">${escapeHtml(item.edge.relationshipType || label)}</span><span>${escapeHtml(item.node.label)}</span></button>`).join("")}</div></div>`).join("")}</div>`;
}

function connectionGroups(node, graph) {
  const groups = { "Inherits / Implements": [], "Outgoing XREFs": [], "Incoming XREFs": [], "Type References": [] };
  const byId = new Map((graph?.nodes || []).map((item) => [item.id, item]));
  (graph?.edges || []).forEach((edge) => {
    if (edge.fromId === node.id) {
      const target = byId.get(edge.toId);
      if (!target) return;
      const relationship = String(edge.relationshipType || "");
       const group = relationship === "Inherits" || relationship === "Implements" ? "Inherits / Implements" : relationship === "TypeReference" ? "Type References" : "Outgoing XREFs";
       groups[group].push({ edge, node: target });
     } else if (edge.toId === node.id) {
       const source = byId.get(edge.fromId);
       if (source) groups["Incoming XREFs"].push({ edge, node: source });
    }
  });
  return groups;
}

function renderFindingRow(finding) {
  return `<button class="finding-row" data-action="select-finding" data-finding-id="${escapeAttr(finding.id)}"><div><span class="severity-pill severity-${String(finding.severity || "info").toLowerCase()}">${escapeHtml(finding.severity)}</span></div><div class="finding-rule">${escapeHtml(finding.ruleId)}</div><div class="finding-message" title="${escapeAttr(finding.message)}">${escapeHtml(finding.message)}</div><div class="finding-source" title="${escapeAttr(finding.relativePath || "")}">${escapeHtml(finding.relativePath || "—")}</div><div class="finding-lines">${formatLines(finding.startLine, finding.endLine)}</div></button>`;
}

function renderComparison(comparison) {
  const delta = (comparison.currentFindingCount ?? 0) - (comparison.baselineFindingCount ?? 0);
  return `<div class="comparison-result"><div class="comparison-counters">${comparisonCounter("New", comparison.newCount, "delta-positive")}${comparisonCounter("Removed", comparison.removedCount, "delta-negative")}${comparisonCounter("Changed", comparison.changedCount, "delta-positive")}${comparisonCounter("Unchanged", comparison.unchangedCount, "delta-neutral")}</div><p class="subtle">${comparison.isReliable ? "Fingerprint comparison is reliable for these runs." : "Comparison has limitations for these runs."} Finding count delta: <strong class="${delta > 0 ? "delta-positive" : delta < 0 ? "delta-negative" : "delta-neutral"}">${delta > 0 ? "+" : ""}${delta}</strong>.</p><div class="comparison-changes">${(comparison.changes || []).slice(0, 80).map((change) => `<div class="comparison-change"><span class="badge">${escapeHtml(change.kind)}</span><div><strong>${escapeHtml(change.currentFinding?.message || change.baselineFinding?.message || "Finding change")}</strong><div>${escapeHtml(change.currentFinding?.relativePath || change.baselineFinding?.relativePath || "No source path")}</div></div></div>`).join("") || emptyState("No finding changes were returned.")}</div></div>`;
}

function renderInspectorContent() {
  const inspector = state.inspector;
  if (!inspector) return `<div class="inspector-empty">Select a symbol, finding, or connection to inspect its details.</div>`;
  if (inspector.type === "finding") return renderFindingInspector(inspector.finding);
  if (inspector.type === "source") return renderSourceInspector(inspector.source, inspector.back);
  if (inspector.type === "edge") return renderEdgeInspector(inspector.edge);
  return renderNodeInspector(inspector.node);
}

function renderNodeInspector(node) {
  const graph = state.graph;
  const sourceAction = node.relativePath ? `<button class="button button-secondary" data-action="view-source" data-node-id="${escapeAttr(node.id)}">View Source</button>` : "";
  return `<div class="entity-heading"><div class="entity-symbol">${escapeHtml(symbolIcon(node.kind))}</div><div><span class="eyebrow">${escapeHtml(node.kind)}</span><h2>${escapeHtml(node.label)}</h2><p>${escapeHtml(node.fullyQualifiedName || node.symbolKey || "Symbol")}</p></div></div><div class="detail-grid">${detailItem("Namespace", node.namespaceName || "(global)")}${detailItem("Containing type", node.containingType || "—")}${detailItem("File", node.relativePath || "—", true)}${detailItem("Lines", formatLines(node.startLine, node.endLine))}${detailItem("Relative line offset", relativeLineOffset(node.startLine, node.endLine))}${detailItem("Incoming XREFs", node.incomingDependencyCount || 0)}${detailItem("Outgoing XREFs", node.outgoingDependencyCount || 0)}${detailItem("Health findings", node.relatedFindingCount || 0)}</div><div class="detail-actions"><button class="button button-primary" data-action="focus-node-graph" data-node-id="${escapeAttr(node.id)}">Focus in Graph</button>${sourceAction}<button class="button button-quiet copy-button" data-action="copy-symbol" data-node-id="${escapeAttr(node.id)}">Copy symbol</button></div>${renderConnectionPanel(node, graph)}`;
}

function renderEdgeInspector(edge) {
  const byId = new Map((state.graph?.nodes || []).map((node) => [node.id, node]));
  const from = byId.get(edge.fromId);
  const to = byId.get(edge.toId);
  return `<div class="entity-heading"><div class="entity-symbol edge-symbol">→</div><div><span class="eyebrow">${escapeHtml(edge.relationshipType || "Dependency")}</span><h2>Connection</h2><p>${escapeHtml(from?.label || edge.fromId)} → ${escapeHtml(to?.label || edge.toId)}</p></div></div><div class="detail-grid">${detailItem("From", from?.fullyQualifiedName || from?.label || edge.fromId, true)}${detailItem("To", to?.fullyQualifiedName || to?.label || edge.toId, true)}${detailItem("Relationship", edge.relationshipType || "Dependency")}${detailItem("Resolved", edge.isResolved ? "Yes" : "No")}${detailItem("Direction", edge.isIncoming ? "Incoming" : edge.isOutgoing ? "Outgoing" : "Unscoped")}</div><div class="detail-actions">${from ? `<button class="button button-secondary" data-action="select-node" data-node-id="${escapeAttr(from.id)}">Inspect source</button>` : ""}${to ? `<button class="button button-secondary" data-action="select-node" data-node-id="${escapeAttr(to.id)}">Inspect target</button>` : ""}</div>`;
}

function renderFindingInspector(finding) {
  return `<div class="entity-heading"><div class="entity-symbol">!</div><div><span class="eyebrow">Diagnostic</span><h2>${escapeHtml(finding.ruleId)}</h2><p><span class="severity-pill severity-${String(finding.severity || "info").toLowerCase()}">${escapeHtml(finding.severity)}</span></p></div></div><div class="detail-grid">${detailItem("Message", finding.message, true)}${detailItem("Source file", finding.relativePath || "—", true)}${detailItem("Line range", formatLines(finding.startLine, finding.endLine))}${detailItem("Symbol", finding.symbolName || "—")}</div><div class="detail-actions"><button class="button button-primary" data-action="view-finding-source" data-finding-id="${escapeAttr(finding.id)}">View Source</button><button class="button button-secondary" data-action="copy-finding" data-finding-id="${escapeAttr(finding.id)}">Copy finding</button></div>`;
}

function renderSourceInspector(source, back) {
  return `<div class="entity-heading"><div class="entity-symbol">⌘</div><div><span class="eyebrow">Source preview</span><h2>${escapeHtml(source.relativePath)}</h2><p>Lines ${source.startLine}–${source.endLine} of ${source.totalLines}</p></div></div><div class="source-preview">${source.lines.map((line) => `<div class="source-line"><span class="source-line-number">${line.lineNumber}</span><span class="source-line-text">${escapeHtml(line.text)}</span></div>`).join("")}</div><div class="detail-actions"><button class="button button-secondary" data-action="copy-source" data-source-text="${escapeAttr(source.lines.map((line) => line.text).join("\n"))}">Copy snippet</button>${back ? `<button class="button button-quiet" data-action="restore-inspector">Back to details</button>` : ""}</div>`;
}

function handleActionClick(event) {
  const target = event.target.closest("[data-action]");
  if (!target) return;
  const action = target.dataset.action;
  if (["tree-toggle", "select-node", "select-edge", "select-project", "select-scan", "run-analysis"].includes(action)) event.stopPropagation();

  switch (action) {
    case "open-onboarding":
      openOnboarding();
      break;
    case "close-modal":
      $("#modal-root").innerHTML = "";
      break;
    case "refresh-projects":
      refreshCurrentView();
      break;
    case "select-project":
      selectProject(target.dataset.projectId);
      break;
    case "select-scan":
      state.selectedScanId = target.dataset.scanId;
      state.selectedNodeId = "";
      state.graph = null;
      state.graphLoadedFor = "";
      state.graphSearchFocusKey = "";
      state.findingsKey = "";
      state.filesLoadedFor = "";
      updateProjectHeader();
      renderRoute();
      break;
    case "run-analysis":
      startAnalysis(target.dataset.scanId);
      break;
    case "tree-toggle":
      toggleTreeNode(target.dataset.treeKey);
      break;
    case "select-node":
      selectNode(target.dataset.nodeId);
      break;
    case "select-edge":
      selectEdge(Number(target.dataset.edgeIndex));
      break;
    case "focus-node-graph":
      focusNodeInGraph(target.dataset.nodeId);
      break;
    case "reload-graph":
      state.graphLoadedFor = "";
      state.graphViewport = null;
      state.graphSearchFocusKey = "";
      renderRoute();
      break;
    case "apply-graph-filters":
      state.graphLoadedFor = "";
      state.graphViewport = null;
      state.graphSearchFocusKey = "";
      renderRoute();
      break;
    case "graph-zoom-in":
      zoomGraph(.78);
      break;
    case "graph-zoom-out":
      zoomGraph(1.28);
      break;
    case "graph-fit":
      fitGraphViewport(state.graph);
      applyGraphViewport();
      break;
    case "graph-reset":
      state.graphViewport = null;
      fitGraphViewport(state.graph);
      applyGraphViewport();
      break;
    case "reload-findings":
      state.findingsKey = "";
      renderRoute();
      break;
    case "apply-health-filters":
      state.findingsKey = "";
      renderRoute();
      break;
    case "clear-health-filters":
      state.findingFilters = { severity: "", rule: "", path: "" };
      state.findingsKey = "";
      renderRoute();
      break;
    case "rule-chip":
      state.findingFilters.rule = target.dataset.rule || "";
      state.findingsKey = "";
      renderRoute();
      break;
    case "select-finding":
      openFindingInspector(target.dataset.findingId);
      break;
    case "view-source":
      showSourceForNode(target.dataset.nodeId);
      break;
    case "view-finding-source":
      showSourceForFinding(target.dataset.findingId);
      break;
    case "copy-symbol":
      copyNodeSymbol(target.dataset.nodeId);
      break;
    case "copy-finding":
      copyFinding(target.dataset.findingId);
      break;
    case "copy-source":
      copyText(target.dataset.sourceText || "");
      break;
    case "restore-inspector":
      restoreInspector();
      break;
    case "close-inspector":
      closeInspector();
      break;
    case "reload-git":
      state.gitLoadedFor = "";
      renderRoute();
      break;
    case "compare-runs":
      state.comparisonKey = "";
      renderRoute();
      break;
    default:
      break;
  }
}

function handleViewInput(event) {
  const action = event.target.dataset.action;
  if (action === "tree-search") {
    state.treeSearch = event.target.value;
    debounceRender();
  } else if (action === "graph-search") {
    state.graphQuery.search = event.target.value;
    debounceRender();
  } else if (action === "health-rule") {
    state.findingFilters.rule = event.target.value;
  } else if (action === "health-path") {
    state.findingFilters.path = event.target.value;
  }
}

function handleViewChange(event) {
  const action = event.target.dataset.action;
  if (action === "graph-kind") state.graphQuery.symbolKind = event.target.value;
  if (action === "graph-relationship") state.graphQuery.relationship = event.target.value;
  if (action === "graph-direction") state.graphQuery.direction = event.target.value;
  if (action === "graph-depth") state.graphQuery.depth = event.target.value;
  if (action === "graph-isolated") state.graphQuery.includeIsolated = event.target.checked;
  if (action === "health-severity") state.findingFilters.severity = event.target.value;
  if (action === "report-baseline") {
    state.reportBaselineId = event.target.value;
    state.comparisonKey = "";
  }
  if (action === "report-current") {
    state.reportCurrentId = event.target.value;
    state.comparisonKey = "";
  }
  if (action === "theme-toggle") toggleTheme();
}

function debounceRender() {
  clearTimeout(state.debounceTimer);
  const activeElement = document.activeElement;
  const activeId = activeElement?.id;
  const selectionStart = activeElement?.selectionStart;
  state.debounceTimer = setTimeout(() => {
    state.graphLoadedFor = "";
    if (state.route === "graph") {
      state.graphViewport = null;
      state.graphSearchFocusKey = "";
    }
    renderRoute().then(() => {
      if (!activeId) return;
      const restored = document.getElementById(activeId);
      if (!restored) return;
      restored.focus();
      if (typeof selectionStart === "number" && restored.setSelectionRange) restored.setSelectionRange(selectionStart, selectionStart);
    });
  }, 320);
}

function toggleTreeNode(key) {
  if (!key) return;
  if (state.treeExpanded.has(key)) state.treeExpanded.delete(key);
  else state.treeExpanded.add(key);
  renderRoute();
}

function selectNode(nodeId) {
  const node = state.graph?.nodes?.find((item) => item.id === nodeId);
  if (!node) return;
  state.selectedNodeId = nodeId;
  state.graphLoadedFor = "";
  state.graphViewport = null;
  openInspector({ type: "node", node });
  renderRoute();
}

function selectEdge(edgeIndex) {
  const edge = state.graph?.edges?.[edgeIndex];
  if (!edge) return;
  openInspector({ type: "edge", edge });
}

function focusNodeInGraph(nodeId) {
  state.selectedNodeId = nodeId;
  state.graphLoadedFor = "";
  state.graphViewport = null;
  closeInspector();
  if (location.hash !== "#graph") history.pushState(null, "", "#graph");
  state.route = "graph";
  renderRoute();
}

function openFindingInspector(findingId) {
  const finding = state.findings.find((item) => item.id === findingId);
  if (!finding) return;
  openInspector({ type: "finding", finding });
}

function openInspector(inspector) {
  state.inspector = inspector;
  $("#inspector-title").textContent = inspector.type === "finding"
    ? inspector.finding.ruleId
    : inspector.type === "source"
      ? inspector.source.relativePath
      : inspector.type === "edge"
        ? inspector.edge.relationshipType || "Connection"
        : inspector.node.label;
  $("#inspector-content").innerHTML = renderInspectorContent();
  $("#inspector-drawer").classList.add("open");
  $("#inspector-drawer").setAttribute("aria-hidden", "false");
}

function closeInspector() {
  state.inspector = null;
  $("#inspector-drawer").classList.remove("open");
  $("#inspector-drawer").setAttribute("aria-hidden", "true");
}

async function showSourceForNode(nodeId) {
  const node = state.graph?.nodes?.find((item) => item.id === nodeId);
  if (!node?.relativePath || !state.selectedProjectId || !state.selectedScanId) return showToast("No source path is associated with this symbol.", true);
  await loadSource(node.relativePath, node.startLine, node.endLine, { type: "node", node });
}

async function showSourceForFinding(findingId) {
  const finding = state.findings.find((item) => item.id === findingId);
  if (!finding?.relativePath || !state.selectedProjectId || !state.selectedScanId) return showToast("No source path is associated with this finding.", true);
  await loadSource(finding.relativePath, finding.startLine, finding.endLine, { type: "finding", finding });
}

async function loadSource(relativePath, startLine, endLine, back) {
  try {
    const params = new URLSearchParams({ path: relativePath });
    if (startLine) params.set("startLine", startLine);
    if (endLine) params.set("endLine", endLine);
    const source = await fetchJson(`/api/projects/${state.selectedProjectId}/scans/${state.selectedScanId}/source?${params}`);
    openInspector({ type: "source", source, back });
  } catch (error) {
    showToast(error.message, true);
  }
}

function restoreInspector() {
  const back = state.inspector?.back;
  if (back) openInspector(back);
}

function copyNodeSymbol(nodeId) {
  const node = state.graph?.nodes?.find((item) => item.id === nodeId);
  if (node) copyText(node.fullyQualifiedName || node.symbolKey || node.label);
}

function copyFinding(findingId) {
  const finding = state.findings.find((item) => item.id === findingId);
  if (finding) copyText(`${finding.ruleId} [${finding.severity}] ${finding.message}\n${finding.relativePath || ""}:${formatLines(finding.startLine, finding.endLine)}`);
}

function decorateGraphView() {
  const toolbar = $(".graph-toolbar");
  if (toolbar && !$("#graph-depth")) {
    toolbar.insertAdjacentHTML("beforeend", `<div class="field graph-depth-field"><label for="graph-depth">Trace depth</label><select id="graph-depth" data-action="graph-depth"><option value="" ${state.graphQuery.depth === "" ? "selected" : ""}>All visible</option><option value="1" ${state.graphQuery.depth === "1" ? "selected" : ""}>1 hop</option><option value="2" ${state.graphQuery.depth === "2" ? "selected" : ""}>2 hops</option><option value="3" ${state.graphQuery.depth === "3" ? "selected" : ""}>3 hops</option><option value="4" ${state.graphQuery.depth === "4" ? "selected" : ""}>4 hops</option></select></div><label class="graph-toggle"><input type="checkbox" data-action="graph-isolated" ${state.graphQuery.includeIsolated ? "checked" : ""}><span>Show isolated</span></label>`);
  }

  const wrap = $(".graph-canvas-wrap");
  if (wrap && !$("#graph-minimap")) {
    wrap.insertAdjacentHTML("beforeend", `<svg id="graph-minimap" class="graph-minimap" role="img" aria-label="Dependency graph mini-map"></svg>`);
  }

  const stats = $(".graph-stats");
  if (stats && state.graph && !$("[data-graph-quality]", stats)) {
    stats.insertAdjacentHTML("beforeend", `${graphStat("Persisted", state.graph.persistedNodeCount ?? state.graph.nodes?.length ?? 0)}${graphStat("Unresolved", state.graph.unresolvedDependencyCount ?? 0)}${graphStat("Missing source", state.graph.missingSourceFileCount ?? 0)}`);
  }

  const legend = $(".legend");
  if (legend && !$("[data-relationship-legend]", legend)) {
    legend.insertAdjacentHTML("beforeend", `<div class="relationship-legend" data-relationship-legend><span><i class="legend-line relationship-calls"></i> Calls</span><span><i class="legend-line relationship-inherits"></i> Inherits / Implements</span><span><i class="legend-line relationship-reference"></i> Type reference</span></div>`);
  }
}

function renderGraphSvg() {
  const svg = $("#graph-canvas");
  if (!svg || !state.graph?.nodes?.length) return;
  svg.replaceChildren();
  const positions = graphPositions(state.graph.nodes);
  if (!state.graphViewport) fitGraphViewport(state.graph);
  const defs = svgElement("defs");
  const relationshipColors = graphRelationshipColors();
  Object.entries(relationshipColors).forEach(([relationship, color]) => {
    const marker = svgElement("marker");
    marker.setAttribute("id", `arrow-${relationship}`);
    marker.setAttribute("viewBox", "0 0 10 10");
    marker.setAttribute("refX", "9");
    marker.setAttribute("refY", "5");
    marker.setAttribute("markerWidth", "6");
    marker.setAttribute("markerHeight", "6");
    marker.setAttribute("orient", "auto-start-reverse");
    const arrow = svgElement("path");
    arrow.setAttribute("d", "M 0 0 L 10 5 L 0 10 z");
    arrow.setAttribute("fill", color);
    marker.appendChild(arrow);
    defs.appendChild(marker);
  });
  svg.appendChild(defs);

  state.graph.edges.forEach((edge, index) => {
    const from = positions.get(edge.fromId);
    const to = positions.get(edge.toId);
    if (!from || !to) return;
    const relationship = graphRelationshipKey(edge.relationshipType);
    const group = svgElement("g");
    group.classList.add("graph-edge-group");
    if (edge.isHighlighted) group.classList.add("highlighted");
    group.dataset.action = "select-edge";
    group.dataset.edgeIndex = String(index);
    group.setAttribute("role", "button");
    group.setAttribute("tabindex", "0");

    const hit = svgElement("path");
    hit.classList.add("graph-edge-hit");
    hit.setAttribute("d", graphEdgePath(from, to));
    group.appendChild(hit);

    const path = svgElement("path");
    path.classList.add("graph-edge", `relationship-${relationship}`);
    if (edge.isHighlighted) path.classList.add("highlighted");
    path.setAttribute("d", graphEdgePath(from, to));
    path.setAttribute("marker-end", `url(#arrow-${relationship})`);
    path.setAttribute("data-label", edge.relationshipType || "Dependency");
    group.appendChild(path);

    const label = svgElement("text");
    label.classList.add("graph-edge-label");
    const midpoint = graphEdgeMidpoint(from, to);
    label.setAttribute("x", `${midpoint.x}`);
    label.setAttribute("y", `${midpoint.y - 5}`);
    label.textContent = shorten(edge.relationshipType || "Dependency", 18);
    group.appendChild(label);
    svg.appendChild(group);
  });

  state.graph.nodes.forEach((node) => {
    const point = positions.get(node.id);
    if (!point) return;
    const group = svgElement("g");
    group.classList.add("graph-node");
    if (node.isSelected || node.id === state.selectedNodeId) group.classList.add("selected");
    if (node.isNeighbor) group.classList.add("neighbor");
    if (node.isIsolated) group.classList.add("isolated");
    if (graphNodeMatchesSearch(node)) group.classList.add("search-hit");
    group.dataset.action = "select-node";
    group.dataset.nodeId = node.id;
    group.setAttribute("transform", `translate(${point.x}, ${point.y})`);
    const title = svgElement("title");
    title.textContent = `${node.label} · ${node.kind}${node.relativePath ? ` · ${node.relativePath}` : ""}`;
    group.appendChild(title);

    const shell = svgElement("rect");
    shell.classList.add("node-shell");
    shell.setAttribute("width", `${GRAPH_NODE_WIDTH}`);
    shell.setAttribute("height", `${GRAPH_NODE_HEIGHT}`);
    shell.setAttribute("rx", "8");
    group.appendChild(shell);

    const header = svgElement("rect");
    header.classList.add("node-header");
    header.setAttribute("width", `${GRAPH_NODE_WIDTH}`);
    header.setAttribute("height", `${GRAPH_HEADER_HEIGHT}`);
    header.setAttribute("rx", "8");
    group.appendChild(header);

    const headerFill = svgElement("rect");
    headerFill.classList.add("node-header-fill");
    headerFill.setAttribute("x", "0");
    headerFill.setAttribute("y", `${GRAPH_HEADER_HEIGHT - 8}`);
    headerFill.setAttribute("width", `${GRAPH_NODE_WIDTH}`);
    headerFill.setAttribute("height", "8");
    group.appendChild(headerFill);

    const icon = svgElement("text");
    icon.classList.add("node-icon");
    icon.setAttribute("x", "10");
    icon.setAttribute("y", "17");
    icon.textContent = symbolIcon(node.kind);
    group.appendChild(icon);

    const label = svgElement("text");
    label.classList.add("node-label");
    label.setAttribute("x", "29");
    label.setAttribute("y", "17");
    label.textContent = shorten(node.label, 25);
    group.appendChild(label);

    const kind = svgElement("text");
    kind.classList.add("node-kind");
    kind.setAttribute("x", `${GRAPH_NODE_WIDTH - 10}`);
    kind.setAttribute("y", "17");
    kind.setAttribute("text-anchor", "end");
    kind.textContent = node.kind;
    group.appendChild(kind);

    const signature = svgElement("text");
    signature.classList.add("node-signature");
    signature.setAttribute("x", "11");
    signature.setAttribute("y", "43");
    signature.textContent = shorten(node.fullyQualifiedName || node.symbolKey || node.label, 34);
    group.appendChild(signature);

    const location = svgElement("text");
    location.classList.add("node-location");
    location.setAttribute("x", "11");
    location.setAttribute("y", "61");
    location.textContent = node.relativePath ? `${shorten(node.relativePath, 27)}:${formatLines(node.startLine, node.endLine)}` : "Source location unavailable";
    group.appendChild(location);

    const counts = svgElement("text");
    counts.classList.add("node-counts");
    counts.setAttribute("x", "11");
    counts.setAttribute("y", "82");
    counts.textContent = `in ${node.incomingDependencyCount || 0} · out ${node.outgoingDependencyCount || 0} · findings ${node.relatedFindingCount || 0}`;
    group.appendChild(counts);

    const leftPort = svgElement("circle");
    leftPort.classList.add("graph-port", "graph-port-in");
    leftPort.setAttribute("cx", "0");
    leftPort.setAttribute("cy", `${GRAPH_NODE_HEIGHT / 2}`);
    leftPort.setAttribute("r", "4");
    group.appendChild(leftPort);
    const rightPort = svgElement("circle");
    rightPort.classList.add("graph-port", "graph-port-out");
    rightPort.setAttribute("cx", `${GRAPH_NODE_WIDTH}`);
    rightPort.setAttribute("cy", `${GRAPH_NODE_HEIGHT / 2}`);
    rightPort.setAttribute("r", "4");
    group.appendChild(rightPort);
    svg.appendChild(group);
  });

  applyGraphViewport();
}

const GRAPH_NODE_WIDTH = 218;
const GRAPH_NODE_HEIGHT = 104;
const GRAPH_HEADER_HEIGHT = 28;

function graphRelationshipKey(value) {
  const normalized = String(value || "Dependency").toLowerCase();
  if (normalized === "calls") return "calls";
  if (normalized === "inherits" || normalized === "implements") return "inherits";
  if (normalized === "typereference" || normalized === "type reference") return "reference";
  return "dependency";
}

function graphRelationshipColors() {
  return {
    calls: "#58a6ff",
    inherits: "#bc8cff",
    reference: "#d29922",
    dependency: "#8b949e"
  };
}

function graphEdgePath(from, to) {
  const startX = from.x + GRAPH_NODE_WIDTH;
  const startY = from.y + GRAPH_NODE_HEIGHT / 2;
  const endX = to.x;
  const endY = to.y + GRAPH_NODE_HEIGHT / 2;
  if (Math.abs(startX - endX) < 12 && Math.abs(startY - endY) < 12) {
    return `M ${startX} ${startY} C ${startX + 72} ${startY - 70}, ${endX - 72} ${endY - 70}, ${endX} ${endY}`;
  }
  const bend = Math.max(55, Math.abs(endX - startX) * .42);
  const direction = endX >= startX ? 1 : -1;
  return `M ${startX} ${startY} C ${startX + bend * direction} ${startY}, ${endX - bend * direction} ${endY}, ${endX} ${endY}`;
}

function graphEdgeMidpoint(from, to) {
  return {
    x: (from.x + GRAPH_NODE_WIDTH + to.x) / 2,
    y: (from.y + to.y + GRAPH_NODE_HEIGHT) / 2
  };
}

function graphNodeMatchesSearch(node) {
  const term = state.graphQuery.search.trim().toLowerCase();
  if (!term) return false;
  return [node.label, node.fullyQualifiedName, node.relativePath, node.symbolKey]
    .some((value) => String(value || "").toLowerCase().includes(term));
}

function attachGraphInteractions() {
  const svg = $("#graph-canvas");
  if (!svg) return;
  svg.addEventListener("pointerdown", (event) => {
    if (event.target.closest(".graph-node, .graph-edge-group")) return;
    const rect = svg.getBoundingClientRect();
    state.graphDragging = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, viewport: { ...state.graphViewport }, rect };
    svg.classList.add("is-dragging");
    svg.setPointerCapture(event.pointerId);
  });
  svg.addEventListener("pointermove", (event) => {
    if (!state.graphDragging) return;
    const drag = state.graphDragging;
    const dx = (event.clientX - drag.x) / drag.rect.width * drag.viewport.w;
    const dy = (event.clientY - drag.y) / drag.rect.height * drag.viewport.h;
    state.graphViewport = { ...drag.viewport, x: drag.viewport.x - dx, y: drag.viewport.y - dy };
    applyGraphViewport();
  });
  const release = () => {
    state.graphDragging = null;
    svg.classList.remove("is-dragging");
  };
  svg.addEventListener("pointerup", release);
  svg.addEventListener("pointercancel", release);
  svg.addEventListener("wheel", (event) => {
    event.preventDefault();
    const rect = svg.getBoundingClientRect();
    const cursorX = state.graphViewport.x + (event.clientX - rect.left) / rect.width * state.graphViewport.w;
    const cursorY = state.graphViewport.y + (event.clientY - rect.top) / rect.height * state.graphViewport.h;
    zoomGraph(event.deltaY < 0 ? .84 : 1.19, cursorX, cursorY);
  }, { passive: false });

  const minimap = $("#graph-minimap");
  if (minimap) {
    minimap.addEventListener("click", (event) => {
      const rect = minimap.getBoundingClientRect();
      const viewBox = minimap.viewBox.baseVal;
      const x = viewBox.x + (event.clientX - rect.left) / rect.width * viewBox.width;
      const y = viewBox.y + (event.clientY - rect.top) / rect.height * viewBox.height;
      const viewport = state.graphViewport || { w: 900, h: 520 };
      state.graphViewport = { ...viewport, x: x - viewport.w / 2, y: y - viewport.h / 2 };
      applyGraphViewport();
    });
  }
}

function graphPositions(nodes) {
  const columns = Math.max(1, Math.ceil(Math.sqrt(nodes.length)));
  const positions = new Map();
  nodes.forEach((node, index) => {
    const fallbackX = (index % columns) * 300 + 90;
    const fallbackY = Math.floor(index / columns) * 160 + 70;
    positions.set(node.id, {
      x: Number.isFinite(node.x) && node.x !== 0 ? node.x : fallbackX,
      y: Number.isFinite(node.y) && node.y !== 0 ? node.y : fallbackY
    });
  });
  return positions;
}

function fitGraphViewport(graph) {
  if (!graph?.nodes?.length) {
    state.graphViewport = { x: 0, y: 0, w: 1000, h: 700 };
    return;
  }
  const positions = Array.from(graphPositions(graph.nodes).values());
  const minX = Math.min(...positions.map((point) => point.x));
  const minY = Math.min(...positions.map((point) => point.y));
  const maxX = Math.max(...positions.map((point) => point.x));
  const maxY = Math.max(...positions.map((point) => point.y));
  state.graphViewport = { x: minX - 100, y: minY - 85, w: Math.max(1050, maxX - minX + GRAPH_NODE_WIDTH + 210), h: Math.max(620, maxY - minY + GRAPH_NODE_HEIGHT + 220) };
}

function applyGraphViewport() {
  const svg = $("#graph-canvas");
  if (svg && state.graphViewport) svg.setAttribute("viewBox", `${state.graphViewport.x} ${state.graphViewport.y} ${state.graphViewport.w} ${state.graphViewport.h}`);
  renderGraphMinimap();
}

function renderGraphMinimap() {
  const mini = $("#graph-minimap");
  if (!mini || !state.graph?.nodes?.length) return;
  const positions = graphPositions(state.graph.nodes);
  const points = Array.from(positions.values());
  const minX = Math.min(...points.map((point) => point.x)) - 35;
  const minY = Math.min(...points.map((point) => point.y)) - 35;
  const maxX = Math.max(...points.map((point) => point.x + GRAPH_NODE_WIDTH)) + 35;
  const maxY = Math.max(...points.map((point) => point.y + GRAPH_NODE_HEIGHT)) + 35;
  mini.setAttribute("viewBox", `${minX} ${minY} ${Math.max(1, maxX - minX)} ${Math.max(1, maxY - minY)}`);
  mini.replaceChildren();

  const background = svgElement("rect");
  background.classList.add("minimap-background");
  background.setAttribute("x", `${minX}`);
  background.setAttribute("y", `${minY}`);
  background.setAttribute("width", `${maxX - minX}`);
  background.setAttribute("height", `${maxY - minY}`);
  mini.appendChild(background);

  positions.forEach((point, nodeId) => {
    const node = state.graph.nodes.find((item) => item.id === nodeId);
    const rect = svgElement("rect");
    rect.classList.add("minimap-node");
    if (node?.isSelected || nodeId === state.selectedNodeId) rect.classList.add("selected");
    rect.setAttribute("x", `${point.x}`);
    rect.setAttribute("y", `${point.y}`);
    rect.setAttribute("width", `${GRAPH_NODE_WIDTH}`);
    rect.setAttribute("height", `${GRAPH_NODE_HEIGHT}`);
    mini.appendChild(rect);
  });

  if (state.graphViewport) {
    const viewport = svgElement("rect");
    viewport.classList.add("minimap-viewport");
    viewport.setAttribute("x", `${state.graphViewport.x}`);
    viewport.setAttribute("y", `${state.graphViewport.y}`);
    viewport.setAttribute("width", `${state.graphViewport.w}`);
    viewport.setAttribute("height", `${state.graphViewport.h}`);
    mini.appendChild(viewport);
  }
}

function focusGraphSearchResult() {
  const term = state.graphQuery.search.trim();
  if (!term || !state.graph?.nodes?.length) return;
  const key = `${state.graphLoadedFor}|${term.toLowerCase()}`;
  if (state.graphSearchFocusKey === key) return;
  const node = state.graph.nodes.find(graphNodeMatchesSearch) || state.graph.nodes[0];
  const point = graphPositions(state.graph.nodes).get(node.id);
  if (!point) return;
  state.graphViewport = {
    x: point.x - 90,
    y: point.y - 70,
    w: Math.max(620, GRAPH_NODE_WIDTH + 180),
    h: Math.max(360, GRAPH_NODE_HEIGHT + 140)
  };
  state.graphSearchFocusKey = key;
  applyGraphViewport();
}

function relativeLineOffset(start, end) {
  if (!start) return "—";
  if (!end || end === start) return "+0 lines";
  return `+${Math.max(0, end - start)} lines`;
}

function zoomGraph(factor, cursorX, cursorY) {
  if (!state.graphViewport) fitGraphViewport(state.graph);
  const viewport = state.graphViewport;
  const centerX = cursorX ?? viewport.x + viewport.w / 2;
  const centerY = cursorY ?? viewport.y + viewport.h / 2;
  const nextW = Math.max(280, Math.min(8000, viewport.w * factor));
  const nextH = Math.max(220, Math.min(6000, viewport.h * factor));
  state.graphViewport = { x: centerX - (centerX - viewport.x) * (nextW / viewport.w), y: centerY - (centerY - viewport.y) * (nextH / viewport.h), w: nextW, h: nextH };
  applyGraphViewport();
}

function loadingView() {
  return `<div class="page-view"><div class="page-header"><div><span class="eyebrow">ForgeDesk</span><h1>Loading workspace</h1><p>Reading persisted project state…</p></div></div><div class="grid grid-3"><div class="panel panel-body"><div class="skeleton" style="height:24px;width:65%"></div><div class="skeleton" style="height:12px;width:90%;margin-top:14px"></div><div class="skeleton" style="height:120px;margin-top:18px"></div></div><div class="panel panel-body"><div class="skeleton" style="height:24px;width:55%"></div><div class="skeleton" style="height:160px;margin-top:18px"></div></div><div class="panel panel-body"><div class="skeleton" style="height:24px;width:70%"></div><div class="skeleton" style="height:160px;margin-top:18px"></div></div></div></div>`;
}

function noSelectionView(title, message) {
  return `<div class="page-view"><div class="page-header"><div><span class="eyebrow">ForgeDesk</span><h1>${escapeHtml(title)}</h1><p>${escapeHtml(message)}</p></div><div class="page-header-actions"><button class="button button-primary" data-action="open-onboarding">＋ Add project</button><a class="button button-secondary" href="#projects">Projects</a></div></div>${emptyProjectPanel()}</div>`;
}

function noAnalysisView(title, message) {
  return `<div class="page-view"><div class="page-header"><div><span class="eyebrow">Selected workspace</span><h1>${escapeHtml(title)}</h1><p>${escapeHtml(message)}</p></div><div class="page-header-actions"><a class="button button-secondary" href="#projects">Back to project</a></div></div><section class="panel"><div class="empty-state"><div><strong>${escapeHtml(message)}</strong><p class="subtle">The existing analysis service must complete before this view can show persisted symbols.</p></div></div></section></div>`;
}

function emptyProjectPanel() {
  return `<section class="panel"><div class="empty-state"><div><strong>No active project</strong><p class="subtle">Onboard a local folder to unlock the workspace views.</p><button class="button button-primary" data-action="open-onboarding">＋ Onboard project</button></div></div></section>`;
}

function errorView(message) {
  return `<div class="page-view"><section class="panel"><div class="empty-state"><div><strong>Unable to load this view</strong><p class="subtle">${escapeHtml(message)}</p><button class="button button-secondary" data-action="refresh-projects">Try again</button></div></div></section></div>`;
}

function metricCard(label, value, detail, tone, key = "") {
  return `<article class="metric-card ${tone}"${key ? ` data-metric="${escapeAttr(key)}"` : ""}><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong><small>${escapeHtml(detail)}</small></article>`;
}

function summaryItem(label, value) {
  return `<div class="scan-summary-item"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`;
}

function metricPair(label, value, key = "") {
  return `<div${key ? ` data-scan-metric="${escapeAttr(key)}"` : ""}><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`;
}

function graphStat(label, value) {
  return `<div class="graph-stat"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`;
}

function gitFact(label, value) {
  return `<div class="git-fact"><span>${escapeHtml(label)}</span><strong title="${escapeAttr(value)}">${escapeHtml(value)}</strong></div>`;
}

function detailItem(label, value, full = false) {
  return `<div class="detail-item ${full ? "full" : ""}"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`;
}

function comparisonCounter(label, value, tone) {
  return `<div class="comparison-counter"><span>${escapeHtml(label)}</span><strong class="${tone}">${escapeHtml(value)}</strong></div>`;
}

function reportLink(label, description, scanId, format) {
  if (!scanId) return `<div class="export-link" style="opacity:.5"><div><strong>${escapeHtml(label)}</strong><span>${escapeHtml(description)}</span></div><span>Unavailable</span></div>`;
  return `<a class="export-link" href="/api/projects/${state.selectedProjectId}/scans/${scanId}/reports/${format}"><div><strong>${escapeHtml(label)}</strong><span>${escapeHtml(description)}</span></div><span>Download ↓</span></a>`;
}

function shortcutRow(label, shortcut) {
  return `<div class="shortcut-row"><span>${escapeHtml(label)}</span><span class="kbd">${escapeHtml(shortcut)}</span></div>`;
}

function emptyState(message) {
  return `<div class="empty-state">${escapeHtml(message)}</div>`;
}

function isTypeKind(kind) {
  return ["Class", "Interface", "Struct", "Enum", "Record"].includes(kind);
}

function symbolIcon(kind) {
  return { Class: "C", Interface: "I", Struct: "S", Enum: "E", Record: "R", Method: "M", Property: "P", Event: "E", Field: "F", Namespace: "N", Unknown: "?" }[kind] || "•";
}

function analysisStatusLabel(value) {
  const normalized = String(value || "NotStarted");
  return normalized === "NotStarted" ? "Not started" : normalized;
}

function analysisStatusBadge(value) {
  const normalized = String(value || "NotStarted");
  const label = analysisStatusLabel(normalized);
  const inFlight = normalized === "Queued" || normalized === "Running";
  return `${inFlight ? '<span class="status-spinner" aria-hidden="true"></span>' : ""}${escapeHtml(label)}`;
}

function deriveNamespace(node) {
  const value = node.fullyQualifiedName || node.symbolKey || "";
  const label = node.label || "";
  if (!value || !label || !value.endsWith(label)) return "(global)";
  return value.slice(0, -label.length).replace(/\.$/, "") || "(global)";
}

function statusTone(value) {
  const normalized = String(value || "").toLowerCase().replaceAll("_", "");
  if (["completed", "clean", "ready", "active"].includes(normalized)) return "green";
  if (["running", "queued", "partial", "dirty", "warning"].includes(normalized)) return "yellow";
  if (["failed", "error", "critical", "unreadable", "cancelled"].includes(normalized)) return "red";
  if (["syntaxonlyfallback", "msbuildsemantic"].includes(normalized)) return "purple";
  if (["notstarted", "unscanned"].includes(normalized)) return "blue";
  return "neutral";
}

function formatDate(value) {
  if (!value) return "Never";
  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) return "—";
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
}

function formatLines(start, end) {
  if (!start && !end) return "—";
  if (start === end || !end) return String(start);
  return `${start ?? "?"}–${end}`;
}

function shorten(value, length) {
  const text = String(value || "");
  return text.length > length ? `${text.slice(0, length - 1)}…` : text;
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>'"]/g, (character) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "'": "&#39;",
    '"': "&quot;"
  })[character]);
}

function escapeAttr(value) {
  return escapeHtml(value).replace(/\r?\n/g, "&#10;");
}

function svgElement(name) {
  return document.createElementNS("http://www.w3.org/2000/svg", name);
}

async function fetchJson(url, options = {}) {
  const response = await fetch(url, options);
  if (response.status === 204) return null;

  const contentType = response.headers.get("content-type") || "";
  const body = await response.text();
  let payload = null;

  try {
    if (body.trim()) payload = JSON.parse(body);
  } catch {
    // Keep a clear client-side error when an old host or SPA fallback returns HTML.
  }

  if (!response.ok) {
    let detail = payload?.error || `Request failed (${response.status})`;
    if (payload?.errors?.length) detail = payload.errors.join(" ");
    if (!payload && (contentType.includes("text/html") || body.trimStart().startsWith("<"))) {
      detail = `The server returned an HTML page instead of JSON for ${url}. Rebuild and restart ForgeDesk.Web.`;
    }
    throw new Error(detail);
  }

  if (payload === null) {
    if (contentType.includes("text/html") || body.trimStart().startsWith("<")) {
      throw new Error(`The server returned an HTML page instead of JSON for ${url}. Rebuild and restart ForgeDesk.Web.`);
    }

    throw new Error(`The server returned an empty or invalid JSON response for ${url}.`);
  }

  return payload;
}

function applyTheme(theme) {
  state.theme = theme === "light" ? "light" : "dark";
  document.documentElement.dataset.theme = state.theme;
  localStorage.setItem("forgedesk-theme", state.theme);
  const headerTheme = $("#header-theme");
  if (headerTheme) {
    headerTheme.textContent = state.theme === "dark" ? "☾" : "☀";
    headerTheme.title = state.theme === "dark" ? "Switch to light theme" : "Switch to dark theme";
  }
}

function toggleTheme() {
  applyTheme(state.theme === "dark" ? "light" : "dark");
  if (state.route === "settings") renderRoute();
}

function applySidebarState() {
  $("#sidebar").classList.toggle("collapsed", state.sidebarCollapsed);
  $("#app-shell").classList.toggle("sidebar-collapsed", state.sidebarCollapsed);
}

function toggleSidebar() {
  state.sidebarCollapsed = !state.sidebarCollapsed;
  localStorage.setItem("forgedesk-sidebar", state.sidebarCollapsed ? "collapsed" : "expanded");
  applySidebarState();
}

async function copyText(value) {
  try {
    await navigator.clipboard.writeText(value);
    showToast("Copied to clipboard.");
  } catch {
    showToast("Clipboard access was not available.", true);
  }
}

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function showToast(message, error = false) {
  const toast = $("#toast");
  toast.textContent = message;
  toast.className = `toast show${error ? " error" : ""}`;
  clearTimeout(state.toastTimer);
  state.toastTimer = setTimeout(() => { toast.className = "toast"; }, 4200);
}
