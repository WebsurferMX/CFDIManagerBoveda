window.cfdiNavigation = {
    esRecarga: () => {
        const nav = performance.getEntriesByType("navigation");

        return nav.length > 0 &&
               nav[0].type === "reload";
    }
};