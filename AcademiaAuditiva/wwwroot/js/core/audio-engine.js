// Audio engine — token-based playback for the anti-cheat flow.
//
// Every exercise plays a SINGLE pre-mixed clip addressed only by an
// opaque round token, so the browser never sees a note name (the legacy
// per-note `/audio/{note}.mp3` sampler leaked answers in DevTools).
// Playback uses the native Web Audio API.
//
// Public API:
//   AudioEngine.playToken(token[, { overlap, onStart }])
//                                 → Promise that resolves when the clip
//                                   finishes playing or is stopped; with
//                                   overlap: true the clips already playing
//                                   keep ringing (Explore's piano keys).
//                                   onStart(clock) is called as the clip
//                                   starts: clock() is how far into it the
//                                   student is hearing, in seconds (RhythmTap
//                                   takes the taps from a moment of its clip)
//   AudioEngine.stop()            → interrupts every clip still playing
//   AudioEngine.preload(token)    → optional hint to fetch the buffer
//                                   without playing yet
//   AudioEngine.setupWaveform(id) → live waveform over a staff, themed via CSS
const AudioEngine = (() => {
    const Context = window.AudioContext || window.webkitAudioContext;
    const FFT_SIZE = 2048;
    // A round produces one or two short clips. Keeping the latest decoded
    // clips makes Replay instant without a second fetch (the server forbids
    // HTTP caching); older rounds are dropped so long sessions stay light.
    const MAX_CACHED_CLIPS = 8;
    const bufferCache = new Map();
    let context = null;
    let output = null;
    let analyser = null;
    const playing = new Set();
    let synchronizedPair = null;
    let playbackGeneration = 0;

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
    // click (or key press) itself keeps the context unlocked for that later
    // call. Pages whose controls play after a fetch mark them with
    // data-aa-audio-unlock.
    const UNLOCK_SELECTOR = "#Play, #Replay, #Melody1, #Melody2, [data-aa-audio-unlock]";
    for (const type of ["click", "keydown"]) {
        document.addEventListener(type, (event) => {
            if (Context && event.target.closest?.(UNLOCK_SELECTOR)) resume();
        }, true);
    }

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
        if (!token) return false;
        try {
            await loadBuffer(token);
            return true;
        } catch (err) {
            console.warn("preload failed", err);
            return false;
        }
    }

    function stop() {
        playbackGeneration += 1;
        const pair = synchronizedPair;
        synchronizedPair = null;
        pair?.resolve?.();
        for (const source of playing) {
            try { source.stop(); } catch { /* already stopped */ }
        }
        playing.clear();
    }

    function setSynchronizedSide(key) {
        const pair = synchronizedPair;
        if (!pair || !pair.keys.has(key)) return false;
        pair.activeKey = key;
        if (pair.gains.length) {
            const audio = getContext();
            Array.from(pair.keys).forEach((clipKey, index) => {
                const gain = pair.gains[index].gain;
                gain.cancelScheduledValues(audio.currentTime);
                gain.setTargetAtTime(clipKey === key ? 1 : 0, audio.currentTime, 0.005);
            });
        }
        return pair.started;
    }

    async function playSynchronized(clips, activeKey, { onStart } = {}) {
        if (!Array.isArray(clips) || clips.length !== 2
            || clips.some(clip => !clip || typeof clip.key !== "string" || typeof clip.token !== "string")
            || !clips.some(clip => clip.key === activeKey)) {
            throw new Error("Synchronized playback requires exactly two keyed clips.");
        }

        const audio = resume();
        stop();
        const pair = {
            keys: new Set(clips.map(clip => clip.key)),
            activeKey,
            started: false,
            sources: [],
            gains: [],
            remaining: clips.length,
            resolve: null
        };
        synchronizedPair = pair;
        const completed = new Promise(resolve => { pair.resolve = resolve; });

        const buffers = await Promise.all(clips.map(clip => loadBuffer(clip.token)));
        if (synchronizedPair !== pair) {
            pair.resolve();
            return completed;
        }

        clips.forEach((clip, index) => {
            const source = audio.createBufferSource();
            const gain = audio.createGain();
            source.buffer = buffers[index];
            gain.gain.value = clip.key === pair.activeKey ? 1 : 0;
            source.connect(gain);
            gain.connect(output);
            pair.sources.push(source);
            pair.gains.push(gain);
            playing.add(source);
            source.onended = () => {
                source.disconnect();
                gain.disconnect();
                playing.delete(source);
                pair.remaining -= 1;
                if (pair.remaining === 0) {
                    if (synchronizedPair === pair) synchronizedPair = null;
                    pair.resolve();
                }
            };
        });

        setSynchronizedSide(pair.activeKey);
        const startAt = audio.currentTime + 0.02;
        pair.started = true;
        pair.sources.forEach(source => source.start(startAt));
        if (typeof onStart === "function") onStart();
        return completed;
    }

    // What the browser knows of the time a sample takes from the render clock
    // (currentTime) to the speakers.
    function latency(audio) {
        return (audio.baseLatency || 0) + (audio.outputLatency || 0);
    }

    async function playToken(token, { overlap = false, onStart } = {}) {
        if (!token) return;
        // Resume before the fetch so a direct click handler still counts as
        // the user gesture.
        const audio = resume();
        const buffer = await loadBuffer(token);

        if (!overlap) stop();

        const source = audio.createBufferSource();
        source.buffer = buffer;
        source.connect(output);
        playing.add(source);

        return new Promise((resolve) => {
            source.onended = () => {
                source.disconnect();
                playing.delete(source);
                resolve();
            };
            source.start();
            if (typeof onStart === "function") {
                const startedAt = audio.currentTime;
                onStart(() => audio.currentTime - startedAt - latency(audio));
            }
        });
    }

    async function playTokenWithEq(token, { frequencyHz, gainDb, q }, { onStart } = {}) {
        if (!token || ![frequencyHz, gainDb, q].every(Number.isFinite)
            || frequencyHz < 20 || frequencyHz > 20000 || gainDb < -12 || gainDb > 12
            || q < 0.25 || q > 8) {
            throw new Error("EQ preview parameters are outside the supported range.");
        }
        const audio = resume();
        stop();
        const generation = playbackGeneration;
        const buffer = await loadBuffer(token);
        const compensation = await eqLoudnessCompensation(buffer, frequencyHz, gainDb, q);
        if (generation !== playbackGeneration) return;

        const source = audio.createBufferSource();
        const filter = audio.createBiquadFilter();
        const normalization = audio.createGain();
        source.buffer = buffer;
        filter.type = "peaking";
        filter.frequency.value = frequencyHz;
        filter.gain.value = gainDb;
        filter.Q.value = q;
        source.connect(filter);
        filter.connect(normalization);
        normalization.gain.value = compensation;
        normalization.connect(output);
        playing.add(source);

        return new Promise((resolve) => {
            source.onended = () => {
                source.disconnect();
                filter.disconnect();
                normalization.disconnect();
                playing.delete(source);
                resolve();
            };
            source.start();
            if (typeof onStart === "function") {
                const startedAt = audio.currentTime;
                onStart(() => audio.currentTime - startedAt - latency(audio));
            }
        });
    }

    async function eqLoudnessCompensation(buffer, frequencyHz, gainDb, q) {
        const OfflineContext = window.OfflineAudioContext || window.webkitOfflineAudioContext;
        if (!OfflineContext) throw new Error("Loudness-matched EQ preview is not supported.");

        const offline = new OfflineContext(buffer.numberOfChannels, buffer.length, buffer.sampleRate);
        const source = offline.createBufferSource();
        const filter = offline.createBiquadFilter();
        source.buffer = buffer;
        filter.type = "peaking";
        filter.frequency.value = frequencyHz;
        filter.gain.value = gainDb;
        filter.Q.value = q;
        source.connect(filter);
        filter.connect(offline.destination);
        source.start();
        const processed = await offline.startRendering();

        let inputEnergy = 0;
        let outputEnergy = 0;
        let outputPeak = 0;
        let sampleCount = 0;
        for (let channel = 0; channel < buffer.numberOfChannels; channel++) {
            const input = buffer.getChannelData(channel);
            const output = processed.getChannelData(channel);
            sampleCount += input.length;
            for (let index = 0; index < input.length; index++) {
                inputEnergy += input[index] * input[index];
                outputEnergy += output[index] * output[index];
                outputPeak = Math.max(outputPeak, Math.abs(output[index]));
            }
        }
        if (outputEnergy === 0 || outputPeak === 0) return 1;

        const inputRms = Math.sqrt(inputEnergy / sampleCount);
        const outputRms = Math.sqrt(outputEnergy / sampleCount);
        return Math.min(inputRms / outputRms, 0.98 / outputPeak, 4);
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
                line: style.getPropertyValue("--aa-wave-line").trim() || "#6650FC",
                staff: style.getPropertyValue("--aa-wave-staff").trim() || "#D5DCE8"
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
        playTokenWithEq,
        playSynchronized,
        setSynchronizedSide,
        preload,
        stop,
        setupWaveform,
        // No-op kept because AcademiaAuditiva.init() still calls it; token
        // playback needs no sampler.
        initSampler: () => {}
    };
})();
