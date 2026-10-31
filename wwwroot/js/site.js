window.cfdiNavigation = {
    esRecarga: function () {
        const entradas =
            performance.getEntriesByType("navigation");

        if (entradas.length > 0) {
            return entradas[0].type === "reload";
        }

        return performance.navigation &&
            performance.navigation.type === 1;
    }
};