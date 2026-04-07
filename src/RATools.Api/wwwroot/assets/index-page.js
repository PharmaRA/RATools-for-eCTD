const { apiGet, checkApiHealth, escapeHtml, formatDate } = window.RAToolsApp;

async function loadApplications() {
  const body = document.getElementById("applications-body");
  const error = document.getElementById("applications-error");
  const empty = document.getElementById("applications-empty");

  error.classList.add("hidden");
  empty.classList.add("hidden");
  body.innerHTML = "";

  try {
    const applications = await apiGet("/api/applications");
    if (!applications.length) {
      empty.classList.remove("hidden");
      return;
    }

    for (const item of applications) {
      const row = document.createElement("tr");
      row.innerHTML = `
        <td>${escapeHtml(item.applicationNumber)}</td>
        <td>${escapeHtml(item.region)}</td>
        <td>${escapeHtml(item.sponsorName)}</td>
        <td>${escapeHtml(formatDate(item.createdUtc))}</td>
        <td>${escapeHtml(item.sequences?.length ?? 0)}</td>
        <td><a class="button link" href="/app.html?applicationId=${encodeURIComponent(item.id)}">View History</a></td>
      `;
      body.appendChild(row);
    }
  } catch (err) {
    error.textContent = err.message;
    error.classList.remove("hidden");
  }
}

window.addEventListener("DOMContentLoaded", async () => {
  await checkApiHealth(document.getElementById("api-status"));
  await loadApplications();
  document.getElementById("refresh-applications").addEventListener("click", loadApplications);
});
