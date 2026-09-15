(function() {
    try {
        const savedTheme = localStorage.getItem('pixelforge_theme') || localStorage.getItem('irt_theme');
        const systemDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        const theme = savedTheme ? savedTheme : (systemDark ? 'dark' : 'light');
        document.documentElement.setAttribute('data-bs-theme', theme);
        document.documentElement.setAttribute('data-theme', theme);
    } catch (e) {
        console.error(e);
    }
})();

