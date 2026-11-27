const modeCards = document.querySelectorAll(".mode-card");

const preloading = document.getElementById("preloading");
const memorySaver = document.getElementById("memorySaver");
const cacheBudget = document.getElementById("cacheBudget");
const parallelPreloads = document.getElementById("parallelPreloads");
const startupPage = document.getElementById("startupPage");
const theme = document.getElementById("theme");
const customNote = document.getElementById("customNote");

const presets = {
  low: {
    preloading: true,
    memorySaver: true,
    cacheBudget: 100,
    parallelPreloads: 1,
  },

  balanced: {
    preloading: true,
    memorySaver: true,
    cacheBudget: 250,
    parallelPreloads: 2,
  },

  performance: {
    preloading: true,
    memorySaver: false,
    cacheBudget: 1000,
    parallelPreloads: 4,
  },
};

const defaultSettings = {
  mode: "balanced",
  ...presets.balanced,
  manualOverride: false,
  startupPage: "home",
  theme: "dark",
};

let settings = { ...defaultSettings };
let bridgeReady = false;

function fromBridgeSettings(data) {
  return {
    mode: data.mode ?? "balanced",
    preloading: data.preloadingEnabled ?? true,
    memorySaver: data.memorySaverEnabled ?? true,
    cacheBudget: data.cacheBudgetMb ?? 250,
    parallelPreloads: data.maxConcurrentPreloads ?? 2,
    manualOverride: data.manualOverride ?? false,
    startupPage: data.startupPage ?? "home",
    theme: data.theme ?? "dark",
  };
}

function toBridgeSettings() {
  return {
    mode: settings.mode,
    preloadingEnabled: settings.preloading,
    memorySaverEnabled: settings.memorySaver,
    cacheBudgetMb: settings.cacheBudget,
    maxConcurrentPreloads: settings.parallelPreloads,
    manualOverride: settings.manualOverride,
    startupPage: settings.startupPage,
    theme: settings.theme,
  };
}

async function saveSettings() {
  if (!bridgeReady) {
    console.error("[Cachd Settings] Bridge is not available.");
    return;
  }

  try {
    const json = JSON.stringify(toBridgeSettings());

    await cachdSystem.saveSettingsJson(json);

    console.log("[Cachd Settings] Saved successfully.");
  } catch (error) {
    console.error("[Cachd Settings] Save failed:", error);
  }
}

function renderSettings() {
  preloading.checked = settings.preloading;
  memorySaver.checked = settings.memorySaver;

  cacheBudget.value = String(settings.cacheBudget);
  parallelPreloads.value = String(settings.parallelPreloads);
  startupPage.value = settings.startupPage;
  theme.value = settings.theme;
  document.documentElement.dataset.theme = settings.theme;

  modeCards.forEach((card) => {
    card.classList.toggle("selected", card.dataset.mode === settings.mode);
  });

  customNote.classList.toggle("visible", settings.mode === "custom");
}
let toastTimer;

function showToast(text, isError = false) {
  const toast = document.getElementById("toast");
  toast.textContent = text;
  toast.classList.toggle("error", isError);
  toast.classList.add("visible");

  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove("visible"), 1500);
}

async function saveSettings() {
  if (!bridgeReady) {
    showToast("Could not save", true);
    return;
  }

  try {
    await cachdSystem.saveSettingsJson(JSON.stringify(toBridgeSettings()));
    showToast("Saved");
  } catch (error) {
    console.error("[Cachd Settings] Save failed:", error);
    showToast("Could not save", true);
  }
}
async function setMode(mode) {
  if (mode === "custom") {
    settings.mode = "custom";
  } else {
    if (!presets[mode]) return;

    settings = {
      ...settings,
      mode,
      ...presets[mode],
    };
  }

  settings.manualOverride = true;

  renderSettings();
  await saveSettings();
}

async function updateCustomSettings() {
  settings = {
    ...settings,
    mode: "custom",
    preloading: preloading.checked,
    memorySaver: memorySaver.checked,
    cacheBudget: Number(cacheBudget.value),
    parallelPreloads: Number(parallelPreloads.value),
    manualOverride: true,
  };

  renderSettings();
  await saveSettings();
}

async function updateStartupPage() {
  settings.startupPage = startupPage.value;
  await saveSettings();

  console.log(`[Cachd Settings] Startup page: ${settings.startupPage}`);
}

async function updateTheme() {
  settings.theme = theme.value;
  renderSettings();
  await saveSettings();
}

modeCards.forEach((card) => {
  card.addEventListener("click", () => {
    setMode(card.dataset.mode);
  });
});

[preloading, memorySaver, cacheBudget, parallelPreloads].forEach((control) => {
  control.addEventListener("change", updateCustomSettings);
});

startupPage.addEventListener("change", updateStartupPage);
theme.addEventListener("change", updateTheme);

const systemRam = document.getElementById("systemRam");
const systemRecommended = document.getElementById("systemRecommended");
const applyRecommended = document.getElementById("applyRecommended");

const modeNames = {
  low: "Low",
  balanced: "Balanced",
  performance: "Performance",
};

let recommendedMode = "balanced";

function getRecommendedMode(ramGb) {
  if (ramGb >= 15) return "performance";
  if (ramGb >= 7) return "balanced";
  return "low";
}
const sections = document.querySelectorAll(".settings-section");
const navLinks = document.querySelectorAll(".nav-item");

const observer = new IntersectionObserver(
  (entries) => {
    entries.forEach((entry) => {
      if (!entry.isIntersecting) return;
      navLinks.forEach((link) =>
        link.classList.toggle(
          "active",
          link.getAttribute("href") === `#${entry.target.id}`,
        ),
      );
    });
  },
  { rootMargin: "-30% 0px -60% 0px" },
);

sections.forEach((s) => observer.observe(s));
function renderSystemBanner(ramGb) {
  const ram = Number(ramGb);

  if (!ramGb || Number.isNaN(ram)) {
    systemRam.textContent = "Unknown";
    systemRecommended.textContent = "Balanced";
    recommendedMode = "balanced";
  } else {
    recommendedMode = getRecommendedMode(ram);
    systemRam.textContent = `${Math.round(ram)} GB`;
    systemRecommended.textContent = modeNames[recommendedMode];
  }

  modeCards.forEach((card) => {
    const isRecommended = card.dataset.mode === recommendedMode;
    card.classList.toggle("recommended", isRecommended);

    const description = card.querySelector(".mode-description");
    if (description && card.dataset.mode !== "custom") {
      description.textContent = isRecommended
        ? "Recommended"
        : description.dataset.default;
    }
  });
}

modeCards.forEach((card) => {
  const description = card.querySelector(".mode-description");
  if (description) description.dataset.default = description.textContent;
});

applyRecommended.addEventListener("click", () => {
  setMode(recommendedMode);
});
async function initializeSettings() {
  try {
    await CefSharp.BindObjectAsync("cachdSystem");

    const json = await cachdSystem.getSettingsJson();
    const stored = JSON.parse(json);

    settings = {
      ...defaultSettings,
      ...fromBridgeSettings(stored),
    };

    bridgeReady = true;

    renderSettings();

    const ramGb = await cachdSystem.getTotalRamGb();
    renderSystemBanner(ramGb);

    console.log(`[Cachd Settings] RAM: ${ramGb} GB`);
    console.log(`[Cachd Settings] Mode: ${settings.mode}`);
    console.log(`[Cachd Settings] Startup page: ${settings.startupPage}`);
    console.log("[Cachd Settings] Loaded from C#.");
  } catch (error) {
    console.error("[Cachd Settings] Initialization failed:", error);

    settings = { ...defaultSettings };
    renderSettings();
    renderSystemBanner(null);
  }
}
function enhanceSelect(select) {
  const wrapper = document.createElement("div");
  wrapper.className = "select";
  select.parentNode.insertBefore(wrapper, select);
  wrapper.appendChild(select);

  const trigger = document.createElement("button");
  trigger.type = "button";
  trigger.className = "select-trigger";
  trigger.setAttribute("aria-haspopup", "listbox");
  trigger.setAttribute("aria-expanded", "false");
  trigger.innerHTML = `
        <span class="select-value"></span>
        <svg class="select-chevron" viewBox="0 0 24 24"><path d="m6 9 6 6 6-6"/></svg>
    `;

  const menu = document.createElement("div");
  menu.className = "select-menu";
  menu.setAttribute("role", "listbox");

  wrapper.append(trigger, menu);

  const valueEl = trigger.querySelector(".select-value");
  let items = [];
  let activeIndex = -1;

  function sync() {
    const option = select.options[select.selectedIndex];
    valueEl.textContent = option ? option.textContent : "";

    items.forEach((el, i) => {
      const selected = i === select.selectedIndex;
      el.classList.toggle("selected", selected);
      el.setAttribute("aria-selected", String(selected));
    });
  }

  function setActive(index) {
    activeIndex = index;
    items.forEach((el, i) => el.classList.toggle("active", i === index));
    items[index]?.scrollIntoView({ block: "nearest" });
  }

  function close() {
    wrapper.classList.remove("open");
    trigger.setAttribute("aria-expanded", "false");
    setActive(-1);
  }

  function open() {
    document.querySelectorAll(".select.open").forEach((w) => w._close());

    const rect = trigger.getBoundingClientRect();
    const menuHeight = menu.offsetHeight;
    const spaceBelow = window.innerHeight - rect.bottom;

    wrapper.classList.toggle(
      "up",
      spaceBelow < menuHeight + 16 && rect.top > menuHeight + 16,
    );
    wrapper.classList.add("open");
    trigger.setAttribute("aria-expanded", "true");
    setActive(select.selectedIndex);
  }

  function choose(index) {
    select.selectedIndex = index;
    sync();
    close();
    select.dispatchEvent(new Event("change", { bubbles: true }));
  }

  items = [...select.options].map((option, i) => {
    const el = document.createElement("div");
    el.className = "select-option";
    el.setAttribute("role", "option");
    el.innerHTML = `
            <span></span>
            <svg class="select-check" viewBox="0 0 24 24"><path d="M20 6 9 17l-5-5"/></svg>
        `;
    el.firstElementChild.textContent = option.textContent;
    el.addEventListener("click", () => choose(i));
    menu.appendChild(el);
    return el;
  });

  trigger.addEventListener("click", () => {
    wrapper.classList.contains("open") ? close() : open();
  });

  trigger.addEventListener("keydown", (event) => {
    const isOpen = wrapper.classList.contains("open");

    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      if (!isOpen) return open();

      const step = event.key === "ArrowDown" ? 1 : -1;
      setActive((activeIndex + step + items.length) % items.length);
    } else if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      if (!isOpen) return open();
      if (activeIndex >= 0) choose(activeIndex);
    } else if (event.key === "Escape" && isOpen) {
      event.preventDefault();
      close();
    }
  });

  const nativeValue = Object.getOwnPropertyDescriptor(
    HTMLSelectElement.prototype,
    "value",
  );
  Object.defineProperty(select, "value", {
    configurable: true,
    get() {
      return nativeValue.get.call(this);
    },
    set(v) {
      nativeValue.set.call(this, v);
      sync();
    },
  });

  wrapper._close = close;
  sync();
}

document.addEventListener("click", (event) => {
  document.querySelectorAll(".select.open").forEach((wrapper) => {
    if (!wrapper.contains(event.target)) wrapper._close();
  });
});

document.querySelectorAll(".setting-card select").forEach(enhanceSelect);
initializeSettings();
