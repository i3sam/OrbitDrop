// Phone menu: a button in the header opens a full-screen sheet that drops from the top, with the header's links
// and the download button. Built from the header itself, so every page gets the same menu.
(() => {
  "use strict";
  const pill = document.querySelector(".nav-pill");
  const links = pill && pill.querySelector("nav");
  if (!pill || !links) return;

  const button = document.createElement("button");
  button.type = "button";
  button.className = "nav-burger";
  button.setAttribute("aria-label", "Menu");
  button.setAttribute("aria-expanded", "false");
  button.setAttribute("aria-controls", "menu-sheet");
  button.innerHTML = "<span></span><span></span>";
  pill.appendChild(button);

  const sheet = document.createElement("div");
  sheet.className = "menu-sheet";
  sheet.id = "menu-sheet";
  sheet.setAttribute("data-lenis-prevent", "");
  sheet.inert = true;
  const list = document.createElement("nav");
  list.setAttribute("aria-label", "Menu");
  links.querySelectorAll("a").forEach((a, i) => {
    const item = document.createElement("a");
    item.href = a.getAttribute("href");
    item.textContent = a.textContent;
    if (a.getAttribute("aria-current")) item.setAttribute("aria-current", "page");
    item.style.setProperty("--i", i);
    list.appendChild(item);
  });
  sheet.appendChild(list);
  const cta = pill.querySelector(".nav-cta");
  if (cta) {
    const get = document.createElement("a");
    get.className = "menu-cta";
    get.href = cta.getAttribute("href");
    get.innerHTML = (cta.querySelector("svg")?.outerHTML ?? "") + "<span>Download on Microsoft Store</span>";
    get.style.setProperty("--i", list.children.length);
    sheet.appendChild(get);
  }
  document.body.appendChild(sheet);

  const set = (open) => {
    document.documentElement.classList.toggle("menu-open", open);
    button.setAttribute("aria-expanded", String(open));
    button.setAttribute("aria-label", open ? "Close menu" : "Menu");
    sheet.inert = !open;
  };
  button.addEventListener("click", () => set(!document.documentElement.classList.contains("menu-open")));
  sheet.addEventListener("click", (e) => { if (e.target.closest("a")) set(false); });
  addEventListener("keydown", (e) => { if (e.key === "Escape" && document.documentElement.classList.contains("menu-open")) { set(false); button.focus(); } });
  matchMedia("(min-width: 901px)").addEventListener("change", (e) => { if (e.matches) set(false); });
})();
