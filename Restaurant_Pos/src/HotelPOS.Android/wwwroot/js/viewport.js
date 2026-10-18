// Phase 22 P3: the tables page defaults to the list on phones (under 600 px) and to the floor map elsewhere.
window.hotelposViewport = {
    isNarrow: function () {
        try { return window.matchMedia('(max-width: 599.98px)').matches; } catch { return false; }
    }
};
