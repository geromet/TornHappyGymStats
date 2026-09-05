window.plotlyInterop = {
    _escapeHandler: null,
    _plotlyPromise: null,

    ensureLoaded: function () {
        if (window.Plotly) return Promise.resolve(window.Plotly);
        if (this._plotlyPromise) return this._plotlyPromise;

        this._plotlyPromise = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = "https://cdn.plot.ly/plotly-2.27.1.min.js";
            script.async = true;
            script.dataset.hgsPlotly = "true";
            script.onload = () => resolve(window.Plotly);
            script.onerror = () => {
                this._plotlyPromise = null;
                reject(new Error("Could not load Plotly."));
            };
            document.head.appendChild(script);
        });

        return this._plotlyPromise;
    },

    render: async function (elementId, traces, layout) {
        const el = document.getElementById(elementId);
        if (!el) return;
        await this.ensureLoaded();
        Plotly.react(el, traces, layout, { responsive: true, displayModeBar: true });
    },
    purge: function (elementId) {
        if (!window.Plotly) return;
        const el = document.getElementById(elementId);
        if (el) Plotly.purge(el);
    },
    resize: function (elementId) {
        if (!window.Plotly) return;
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
