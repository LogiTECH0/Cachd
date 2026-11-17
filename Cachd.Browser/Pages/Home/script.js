const clock = document.getElementById("clock");
const date = document.getElementById("date");

function updateClock() {
    const now = new Date();

    clock.textContent = now.toLocaleTimeString("uk-UA", {
        hour: "2-digit",
        minute: "2-digit",
        hour12: false
    });

    date.textContent = now.toLocaleDateString("uk-UA", {
        weekday: "long",
        day: "numeric",
        month: "long"
    });
}

updateClock();
setInterval(updateClock, 1000);

document.getElementById("searchForm").addEventListener("submit", (event) => {
    event.preventDefault();

    const query = document.getElementById("searchInput").value.trim();

    if (!query) return;

    window.location.href =
        "https://www.google.com/search?q=" + encodeURIComponent(query);
});

function renderMostVisited(sites) {
    const container = document.getElementById("mostVisited");

    if (!container) return;

    container.innerHTML = "";

    if (!sites || sites.length === 0) {
        container.innerHTML = `
            <div class="empty-shortcuts">
                Your most visited pages will appear here.
            </div>
        `;
        return;
    }

    sites.forEach(site => {
        const link = document.createElement("a");
        link.className = "shortcut";
        link.href = site.url;

        const icon = document.createElement("div");
        icon.className = "shortcut-icon";

        const favicon = document.createElement("img");
        favicon.src = site.favicon;
        favicon.alt = "";
        favicon.loading = "lazy";

        favicon.onerror = () => {
            favicon.remove();
            icon.textContent = site.title.charAt(0).toUpperCase();
        };

        icon.appendChild(favicon);

        const title = document.createElement("span");
        title.textContent = site.title;

        const visits = document.createElement("small");
        visits.textContent = `${site.visits} visits`;

        link.appendChild(icon);
        link.appendChild(title);
        link.appendChild(visits);

        container.appendChild(link);
    });
}
