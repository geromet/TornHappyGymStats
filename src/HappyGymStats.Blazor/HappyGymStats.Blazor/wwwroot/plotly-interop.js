window.plotlyInterop = {
    _escapeHandler: null,

    render: function (elementId, traces, layout) {
        const el = document.getElementById(elementId);
        if (!el) return;
        Plotly.react(el, traces, layout, { responsive: true, displayModeBar: true });
    },
    purge: function (elementId) {
        const el = document.getElementById(elementId);
        if (el) Plotly.purge(el);
    },
    resize: function (elementId) {
        const el = document.getElementById(elementId);
        if (!el) return;
        Plotly.Plots.resize(el);
    },
    bindEscape: function (dotNetRef) {
        this.unbindEscape();
        this._escapeHandler = async (event) => {
            if (event.key !== "Escape") return;
            const fullscreenCard = document.querySelector(".gym-fullscreen");
            if (!fullscreenCard) return;
            await dotNetRef.invokeMethodAsync("ExitGymFullscreenFromEscapeAsync");
        };
        window.addEventListener("keydown", this._escapeHandler);
    },
    unbindEscape: function () {
        if (this._escapeHandler) {
            window.removeEventListener("keydown", this._escapeHandler);
            this._escapeHandler = null;
        }
    }
};
