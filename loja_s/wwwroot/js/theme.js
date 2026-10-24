// Modo claro/escuro: respeita a preferência salva; sem preferência, segue o sistema.
(() => {
    const root = document.documentElement;
    const KEY = "felibow-theme";
    const read = () => { try { return localStorage.getItem(KEY); } catch { return null; } };
    const write = v => { try { localStorage.setItem(KEY, v); } catch { /* sem armazenamento */ } };
    const systemDark = () => window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;

    const apply = theme => root.setAttribute("data-theme", theme);
    apply(read() || (systemDark() ? "dark" : "light"));

    document.addEventListener("DOMContentLoaded", () => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "theme-toggle";

        const refresh = () => {
            const dark = root.getAttribute("data-theme") === "dark";
            button.textContent = dark ? "☀️" : "🌙";
            button.setAttribute("aria-label", dark ? "Ativar modo claro" : "Ativar modo escuro");
            button.setAttribute("title", dark ? "Modo claro" : "Modo escuro");
        };

        button.addEventListener("click", () => {
            const next = root.getAttribute("data-theme") === "dark" ? "light" : "dark";
            apply(next);
            write(next);
            refresh();
        });

        refresh();
        document.body.appendChild(button);
    });
})();
