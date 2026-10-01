// Audio engine — token-based playback for the anti-cheat flow.
//
// The legacy build wired Tone.Sampler to a public `/audio/{note}.mp3`
// endpoint, which leaked the question identity in DevTools. The new
// flow keeps Tone.js on the page (so the waveform visualisation still
// works), but every exercise plays a SINGLE pre-mixed clip addressed
// only by an opaque round token. The browser never sees a note name.
//
// Public API:
//   AudioEngine.playToken(token)  → Promise that resolves when the clip
//                                   finishes playing
//   AudioEngine.stop()            → interrupts the currently playing clip
//   AudioEngine.preload(token)    → optional hint to fetch the buffer
//                                   without playing yet
//   AudioEngine.setupWaveform(id) → live waveform over a staff, themed via CSS
const AudioEngine = (() => {
    // A round produces one or two tokens; both are short clips. Caching
    // the decoded buffer by token lets Replay be instant without a
    // second fetch (and the server already declines to cache server-side).
    const bufferCache = new Map();
    let currentSource = null;
    let started = false;

    async function ensureContext() {
        if (!started) {
            try {
                await Tone.start();
            } catch (err) {
                // Tone.start throws when called outside a user gesture;
                // first click on Play always provides one, but defensive
                // logging helps diagnose if a future flow regresses.
                console.warn("Tone.start() failed:", err);
            }
            started = true;
        }
    }

    async function loadBuffer(token) {
        if (bufferCache.has(token)) {
            return bufferCache.get(token);
        }
        const resp = await fetch(`/audio/token/${encodeURIComponent(token)}`, {
            credentials: "same-origin",
            cache: "no-store"
        });
        if (!resp.ok) {
            throw new Error(`Audio fetch failed: ${resp.status}`);
        }
        const arrayBuffer = await resp.arrayBuffer();
        const audioBuffer = await Tone.context.decodeAudioData(arrayBuffer);
        bufferCache.set(token, audioBuffer);
        return audioBuffer;
    }

    async function preload(token) {
        if (!token) return;
        try { await loadBuffer(token); } catch (err) { console.warn("preload failed", err); }
    }

    function stop() {
        if (currentSource) {
            try { currentSource.stop(); } catch { /* already stopped */ }
            currentSource = null;
        }
    }

    async function playToken(token) {
        if (!token) return;
        await ensureContext();
        const buffer = await loadBuffer(token);

        stop();

        // Use Tone.ToneBufferSource so the node integrates with the Tone
        // graph (and setupWaveform's analyser, which is wired off
        // Tone.Destination). A native createBufferSource() can't connect
        // to Tone.Destination directly — Tone's internal lookup throws
        // "A value with the given key could not be found".
        const source = new Tone.ToneBufferSource(buffer).toDestination();
        currentSource = source;

        return new Promise((resolve) => {
            source.onended = () => {
                if (currentSource === source) currentSource = null;
                resolve();
            };
            source.start();
        });
    }

    // === WAVEFORM VISUAL ===
    // A live trace of the output drawn over five staff lines. Colours come from
    // the --aa-wave-line / --aa-wave-staff custom properties of the container, so
    // the canvas follows the light/dark theme.
    let waveformCanvas = null;
    let analyser = null;

    function setupWaveform(targetId = "waveform") {
        const container = document.getElementById(targetId);
        if (!container || waveformCanvas) return;

        waveformCanvas = document.createElement("canvas");
        waveformCanvas.setAttribute("aria-hidden", "true");
        container.appendChild(waveformCanvas);

        analyser = Tone.context.createAnalyser();
        analyser.fftSize = 2048;

        Tone.Destination.connect(analyser);
        animateWaveform(container);
    }

    function animateWaveform(container) {
        if (!waveformCanvas || !analyser) return;

        const canvas = waveformCanvas;
        const ctx = canvas.getContext("2d");
        const data = new Uint8Array(analyser.fftSize);
        let colors = readColors();
        let dirty = true;

        function readColors() {
            const style = getComputedStyle(container);
            return {
                line: style.getPropertyValue("--aa-wave-line").trim() || "#4F46E5",
                staff: style.getPropertyValue("--aa-wave-staff").trim() || "#D9DCEA"
            };
        }

        function resize() {
            const dpr = window.devicePixelRatio || 1;
            const width = Math.max(1, Math.round(container.clientWidth * dpr));
            const height = Math.max(1, Math.round(container.clientHeight * dpr));
            if (canvas.width !== width || canvas.height !== height) {
                canvas.width = width;
                canvas.height = height;
            }
            dirty = true;
        }

        if (window.ResizeObserver) {
            new ResizeObserver(resize).observe(container);
        }
        new MutationObserver(() => {
            colors = readColors();
            dirty = true;
        }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-bs-theme"] });
        resize();

        function draw() {
            requestAnimationFrame(draw);
            analyser.getByteTimeDomainData(data);

            let silent = true;
            for (let i = 0; i < data.length; i++) {
                if (data[i] !== 128) { silent = false; break; }
            }
            // While silent, only redraw after a resize or theme change.
            if (silent && !dirty) return;
            dirty = !silent;

            const dpr = window.devicePixelRatio || 1;
            const width = canvas.width;
            const height = canvas.height;
            ctx.clearRect(0, 0, width, height);

            const staffWidth = Math.max(1, Math.round(dpr));
            const offset = staffWidth % 2 ? 0.5 : 0;
            ctx.lineWidth = staffWidth;
            ctx.strokeStyle = colors.staff;
            ctx.beginPath();
            for (let line = 1; line <= 5; line++) {
                const y = Math.round(height * line / 6) + offset;
                ctx.moveTo(0, y);
                ctx.lineTo(width, y);
            }
            ctx.stroke();

            ctx.lineWidth = 2 * dpr;
            ctx.lineJoin = "round";
            ctx.strokeStyle = colors.line;
            ctx.beginPath();
            const step = width / (data.length - 1);
            for (let i = 0; i < data.length; i++) {
                const y = (data[i] / 128) * (height / 2);
                if (i === 0) {
                    ctx.moveTo(0, y);
                } else {
                    ctx.lineTo(i * step, y);
                }
            }
            ctx.stroke();
        }

        draw();
    }

    return {
        playToken,
        preload,
        stop,
        setupWaveform,
        // No-op kept because AcademiaAuditiva.init() still calls it; token
        // playback needs no sampler.
        initSampler: () => {}
    };
})();
