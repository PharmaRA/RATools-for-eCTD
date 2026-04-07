window.RAToolsApp = (() => {
  function setApiStatus(element, ok, text) {
    if (!element) {
      return;
    }

    element.textContent = text;
    element.classList.remove("ok", "error");
    element.classList.add(ok ? "ok" : "error");
  }

  async function apiGet(url) {
    const response = await fetch(url, {
      headers: {
        Accept: "application/json"
      }
    });

    if (!response.ok) {
      const body = await response.text();
      throw new Error(body || `Request failed: ${response.status}`);
    }

    return response.json();
  }

  function formatDate(value) {
    if (!value) {
      return "-";
    }

    return new Date(value).toLocaleString();
  }

  function escapeHtml(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#39;");
  }

  async function checkApiHealth(statusElement) {
    try {
      const health = await apiGet("/health");
      setApiStatus(statusElement, health.status === "ok", health.status === "ok" ? "API OK" : "API Error");
    } catch {
      setApiStatus(statusElement, false, "API Error");
    }
  }

  return {
    apiGet,
    checkApiHealth,
    escapeHtml,
    formatDate
  };
})();
