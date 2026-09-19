(() => {
    const storageKey = "local-ip-manager-theme";
    const root = document.documentElement;

    function getSavedTheme() {
        try {
            return localStorage.getItem(storageKey);
        } catch {
            return null;
        }
    }

    function saveTheme(theme) {
        try {
            localStorage.setItem(storageKey, theme);
        } catch {
            // The theme still works for this session when storage is unavailable.
        }
    }

    function applyTheme(isDark) {
        root.classList.toggle("dark", isDark);
        return isDark;
    }

    const savedTheme = getSavedTheme();
    const prefersDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
    applyTheme(savedTheme ? savedTheme === "dark" : prefersDark);

    window.localIpTheme = {
        isDark() {
            return root.classList.contains("dark");
        },

        toggle() {
            const isDark = applyTheme(!root.classList.contains("dark"));
            saveTheme(isDark ? "dark" : "light");
            return isDark;
        }
    };
})();
