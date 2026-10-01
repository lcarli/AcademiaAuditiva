// Audio engine — token-based playback for the anti-cheat flow.
//
// Every exercise plays a SINGLE pre-mixed clip addressed only by an
// opaque round token, so the browser never sees a note name (the legacy
// per-note `/audio/{note}.mp3` sampler leaked answers in DevTools).
// Playback uses the native Web Audio API.
//
// Public API:
//   AudioEngine.playToken(token)  → Promise that resolves when the clip
//                                   finishes playing or is stopped
//   AudioEngine.stop()            → interrupts the currently playing clip
//   AudioEngine.preload(token)    → optional hint to fetch the buffer
//                                   without playing yet
//   AudioEngine.setupWaveform(id) → live waveform over a staff, themed via CSS
const AudioEngine = (() => {
    const Context = window.AudioContext || window.webkitAudioContext;
    const FFT_SIZE = 2048;
    // A round produces one or two short clips. Keeping the latest decoded
    // clips makes Replay instant without a second fetch (the server forbids
    // HTTP caching); older rounds are dropped so long sessions stay light.
    const MAX_CACHED_CLIPS = 4;
    const bufferCache = new Map();
    let context = null;
    let output = null;
    let analyser = null;
    let currentSource = null;

    // Created on first use so pages don't open an audio device before the
    // learner asks for sound. Clips play through one gain node, which the
    // waveform analyser taps.
    function getContext() {
        if (!context) {
            if (!Context) throw new Error("Web Audio is not supported in this browser.");
            context = new Context();
            output = context.createGain();
            output.connect(context.destination);
            analyser = context.createAnalyser();
            analyser.fftSize = FFT_SIZE;
            output.connect(analyser);
        }
        return context;
    }

    function resume() {
        const audio = getContext();
        if (audio.state !== "running") {
            audio.resume().catch((err) => console.warn("AudioContext.resume() failed:", err));
        }
        return audio;
    }

    // Safari only starts audio from inside a user gesture, but the Play
    // buttons fetch the round before calling playToken. Resuming on the
    // click itself keeps the context unlocked for that later call.
    document.addEventListener("click", (event) => {
        if (Context && event.target.closest?.("#Play, #Replay, #Melody1, #Melody2")) resume();
    }, true);

    // Caches the pending decode, so a preload and a play of the same token
    // share one request.
    function loadBuffer(token) {
        let pending = bufferCache.get(token);
        if (!pending) {
            pending = fetchClip(token);
            bufferCache.set(token, pending);
            pending.catch(() => {
                if (bufferCache.get(token) === pending) bufferCache.delete(token);
            });
            while (bufferCache.size > MAX_CACHED_CLIPS) {
                bufferCache.delete(bufferCache.keys().next().value);
            }
        }
        return pending;
    }

    async function fetchClip(token) {
        const resp = await fetch(`/audio/token/${encodeURIComponent(token)}`, {
            credentials: "same-origin",
            cache: "no-store"
        });
        if (!resp.ok) {
            throw new Error(`Audio fetch failed: ${resp.status}`);
        }
        return getContext().decodeAudioData(await resp.arrayBuffer());
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
        // Resume before the fetch so a direct click handler still counts as
        // the user gesture.
        const audio = resume();
        const buffer = await loadBuffer(token);

        stop();

        const source = audio.createBufferSource();
        source.buffer = buffer;
        source.connect(output);
        currentSource = source;

        return new Promise((resolve) => {
            source.onended = () => {
                source.disconnect();
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

    function setupWaveform(targetId = "waveform") {
        const container = document.getElementById(targetId);
        if (!container || waveformCanvas) return;

        waveformCanvas = document.createElement("canvas");
        waveformCanvas.setAttribute("aria-hidden", "true");
        container.appendChild(waveformCanvas);
        animateWaveform(container);
    }

    function animateWaveform(container) {
        const canvas = waveformCanvas;
        const ctx = canvas.getContext("2d");
        // Flat (silent) until the first clip creates the analyser.
        const data = new Uint8Array(FFT_SIZE).fill(128);
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
            if (analyser) analyser.getByteTimeDomainData(data);

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
