const { apiGet, checkApiHealth, escapeHtml, formatDate } = window.RAToolsApp;

const state = {
  applicationId: null,
  page: 1,
  pageSize: 20
};

function getFilterValues() {
  return {
    sequenceNumber: document.getElementById("filter-sequence").value.trim(),
    status: document.getElementById("filter-status").value,
    createdFromUtc: document.getElementById("filter-created-from").value,
    createdToUtc: document.getElementById("filter-created-to").value
  };
}

function buildHistoryUrl() {
  const params = new URLSearchParams();
  const filters = getFilterValues();

  if (filters.sequenceNumber) params.set("sequenceNumber", filters.sequenceNumber);
  if (filters.status) params.set("status", filters.status);
  if (filters.createdFromUtc) params.set("createdFromUtc", new Date(filters.createdFromUtc).toISOString());
  if (filters.createdToUtc) params.set("createdToUtc", new Date(filters.createdToUtc).toISOString());
  params.set("page", String(state.page));
  params.set("pageSize", String(state.pageSize));

  return `/api/applications/${encodeURIComponent(state.applicationId)}/publish-history?${params.toString()}`;
}

function setSummary(summary) {
  document.getElementById("summary-completed").textContent = summary?.completedCount ?? 0;
  document.getElementById("summary-failed").textContent = summary?.failedCount ?? 0;
  document.getElementById("summary-running").textContent = summary?.runningCount ?? 0;
}

async function loadApplication() {
  const application = await apiGet(`/api/applications/${encodeURIComponent(state.applicationId)}`);
  document.getElementById("application-title").textContent = `${application.applicationNumber} Publish History`;
  document.getElementById("application-meta").textContent = `${application.region} / ${application.sponsorName}`;
}

async function showReport(jobId) {
  const detailError = document.getElementById("detail-error");
  const detailEmpty = document.getElementById("detail-empty");
  const reportDetail = document.getElementById("report-detail");
  const artifactsDetail = document.getElementById("artifacts-detail");

  detailError.classList.add("hidden");
  detailEmpty.classList.add("hidden");
  artifactsDetail.classList.add("hidden");
  reportDetail.classList.remove("hidden");

  try {
    const report = await apiGet(`/api/publish-jobs/${encodeURIComponent(jobId)}/report`);
    document.getElementById("detail-title").textContent = `Report ${jobId}`;
    reportDetail.innerHTML = `
      <div class="detail-section">
        <div class="detail-grid">
          <div class="detail-item"><strong>Report Version</strong>${escapeHtml(report.reportVersion)}</div>
          <div class="detail-item"><strong>Validation Profile</strong>${escapeHtml(report.validationProfile)}</div>
          <div class="detail-item"><strong>Duration (ms)</strong>${escapeHtml(report.durationMs)}</div>
          <div class="detail-item"><strong>Message</strong>${escapeHtml(report.message ?? "-")}</div>
          <div class="detail-item"><strong>Errors</strong>${escapeHtml(report.errorCount)}</div>
          <div class="detail-item"><strong>Warnings</strong>${escapeHtml(report.warningCount)}</div>
          <div class="detail-item"><strong>Warning Summary</strong>${escapeHtml(report.warningSummary ?? "-")}</div>
          <div class="detail-item"><strong>Report Path</strong>${escapeHtml(report.reportPath ?? "-")}</div>
        </div>
      </div>
      <div class="detail-section">
        <a class="button" href="/api/publish-jobs/${encodeURIComponent(jobId)}/artifacts/PublishReport/download">Download Report</a>
      </div>
    `;
  } catch (err) {
    detailError.textContent = err.message;
    detailError.classList.remove("hidden");
  }
}

async function showArtifacts(jobId) {
  const detailError = document.getElementById("detail-error");
  const detailEmpty = document.getElementById("detail-empty");
  const reportDetail = document.getElementById("report-detail");
  const artifactsDetail = document.getElementById("artifacts-detail");

  detailError.classList.add("hidden");
  detailEmpty.classList.add("hidden");
  reportDetail.classList.add("hidden");
  artifactsDetail.classList.remove("hidden");

  try {
    const artifacts = await apiGet(`/api/publish-jobs/${encodeURIComponent(jobId)}/artifacts`);
    document.getElementById("detail-title").textContent = `Artifacts ${jobId}`;
    artifactsDetail.innerHTML = `
      <div class="artifact-list">
        ${artifacts.artifacts.map((artifact) => `
          <div class="detail-item">
            <strong>${escapeHtml(artifact.name)}</strong>
            <div>Exists: ${escapeHtml(artifact.exists)}</div>
            <div>Size: ${escapeHtml(artifact.sizeBytes)}</div>
            <div>Content Type: ${escapeHtml(artifact.contentType)}</div>
            <div><a class="button link" href="/api/publish-jobs/${encodeURIComponent(jobId)}/artifacts/${encodeURIComponent(artifact.name)}/download">Download</a></div>
          </div>
        `).join("")}
      </div>
    `;
  } catch (err) {
    detailError.textContent = err.message;
    detailError.classList.remove("hidden");
  }
}

async function loadHistory() {
  const body = document.getElementById("history-body");
  const error = document.getElementById("history-error");
  error.classList.add("hidden");
  body.innerHTML = "";

  try {
    const history = await apiGet(buildHistoryUrl());
    setSummary(history.statusSummary);
    document.getElementById("history-meta").textContent = `Total ${history.totalCount} item(s)`;
    document.getElementById("page-indicator").textContent = `Page ${history.page}`;

    for (const entry of history.entries) {
      const row = document.createElement("tr");
      row.innerHTML = `
        <td>${escapeHtml(formatDate(entry.createdUtc))}</td>
        <td>${escapeHtml(entry.sequenceNumber)}</td>
        <td>${escapeHtml(entry.status)}</td>
        <td>${escapeHtml(entry.validationProfile ?? "-")}</td>
        <td>${escapeHtml(entry.errorCount ?? 0)}</td>
        <td>${escapeHtml(entry.warningCount ?? 0)}</td>
        <td>${entry.reportAvailable ? (entry.reportReadable ? "Ready" : "Corrupted") : "Missing"}</td>
        <td>${entry.packagePath ? "Ready" : "-"}</td>
        <td>
          <button class="button link" data-action="report" data-id="${escapeHtml(entry.publishJobId)}">View Report</button>
          <button class="button link" data-action="artifacts" data-id="${escapeHtml(entry.publishJobId)}">Artifacts</button>
        </td>
      `;
      body.appendChild(row);
    }

    body.querySelectorAll("button[data-action]").forEach((button) => {
      button.addEventListener("click", async () => {
        const jobId = button.getAttribute("data-id");
        if (button.getAttribute("data-action") === "report") {
          await showReport(jobId);
        } else {
          await showArtifacts(jobId);
        }
      });
    });

    document.getElementById("prev-page").disabled = state.page <= 1;
    document.getElementById("next-page").disabled = state.page * state.pageSize >= history.totalCount;
  } catch (err) {
    error.textContent = err.message;
    error.classList.remove("hidden");
  }
}

window.addEventListener("DOMContentLoaded", async () => {
  const params = new URLSearchParams(window.location.search);
  state.applicationId = params.get("applicationId");

  if (!state.applicationId) {
    document.getElementById("history-error").textContent = "Missing applicationId query parameter.";
    document.getElementById("history-error").classList.remove("hidden");
    return;
  }

  await checkApiHealth(document.getElementById("api-status"));
  await loadApplication();
  await loadHistory();

  document.getElementById("history-filters").addEventListener("submit", async (event) => {
    event.preventDefault();
    state.page = 1;
    await loadHistory();
  });

  document.getElementById("reset-filters").addEventListener("click", async () => {
    document.getElementById("history-filters").reset();
    state.page = 1;
    await loadHistory();
  });

  document.getElementById("prev-page").addEventListener("click", async () => {
    if (state.page > 1) {
      state.page -= 1;
      await loadHistory();
    }
  });

  document.getElementById("next-page").addEventListener("click", async () => {
    state.page += 1;
    await loadHistory();
  });
});
