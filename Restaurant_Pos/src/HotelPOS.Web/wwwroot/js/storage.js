// Small bridge for the Blazor client: localStorage (device identity, drafts, preferences) wrapped in try/catch because
// private windows and cleared site data can make it throw. Nothing secret is ever stored here.
window.hotelposStorage = {
    get: function (key) {
        try { return window.localStorage.getItem(key); } catch { return null; }
    },
    set: function (key, value) {
        try { window.localStorage.setItem(key, value); return true; } catch { return false; }
    },
    remove: function (key) {
        try { window.localStorage.removeItem(key); } catch { }
    },
    keys: function (prefix) {
        try {
            var result = [];
            for (var i = 0; i < window.localStorage.length; i++) {
                var k = window.localStorage.key(i);
                if (k && k.indexOf(prefix) === 0) { result.push(k); }
            }
            return result;
        } catch { return []; }
    }
};
