window.plotlyInterop = {
    render: function (elementId, traces, layout) {
        const el = document.getElementById(elementId);
        if (!el) return;
        Plotly.react(el, traces, layout, { responsive: true, displayModeBar: true });
    },
    purge: function (elementId) {
        const el = document.getElementById(elementId);
        if (el) Plotly.purge(el);
    }
};
