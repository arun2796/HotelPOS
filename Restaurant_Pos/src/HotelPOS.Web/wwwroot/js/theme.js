// Theme bridge: sets data-theme on <html> ("light" | "dark"); "system" removes it so prefers-color-scheme applies.
window.hotelposTheme = {
    set: function (mode) {
        var root = document.documentElement;
        if (mode === "light" || mode === "dark") { root.setAttribute("data-theme", mode); }
        else { root.removeAttribute("data-theme"); }
    }
};
