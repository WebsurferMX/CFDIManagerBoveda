window.cfdiTheme = {
    getPreferredTheme: function () {
        const savedTheme =
            localStorage.getItem("cfdi-theme");

        if (
            savedTheme === "dark" ||
            savedTheme === "light"
        ) {
            return savedTheme;
        }

        // Sin preferencia guardada se queda en oscuro, como siempre ha abierto la app.
        return "dark";
    },

    current: function () {
        return document.documentElement.getAttribute(
            "data-theme"
        ) || "light";
    },

    applyTheme: function (theme) {
        const selectedTheme =
            theme === "dark"
                ? "dark"
                : "light";

        document.documentElement.setAttribute(
            "data-theme",
            selectedTheme
        );

        document.documentElement.setAttribute(
            "data-bs-theme",
            selectedTheme
        );

        localStorage.setItem(
            "cfdi-theme",
            selectedTheme
        );

        return selectedTheme;
    },

    initialize: function () {
        const theme =
            this.getPreferredTheme();

        return this.applyTheme(theme);
    },

    toggle: function () {
        const currentTheme =
            this.current();

        const newTheme =
            currentTheme === "dark"
                ? "light"
                : "dark";

        return this.applyTheme(newTheme);
    }
};
