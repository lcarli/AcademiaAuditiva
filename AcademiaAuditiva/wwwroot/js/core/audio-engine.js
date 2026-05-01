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
//   AudioEngine.setupWaveform(id) → unchanged visual hook
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

    // === WAVEFORM VISUAL === (unchanged behaviour)
    let waveformCanvas;
    let analyser;
    let audioContext = Tone.context;

    function setupWaveform(targetId = "waveform") {
        const container = document.getElementById(targetId);
        if (!container) return;

        waveformCanvas = document.createElement("canvas");
        waveformCanvas.width = container.clientWidth;
        waveformCanvas.height = container.clientHeight;
        container.appendChild(waveformCanvas);

        analyser = audioContext.createAnalyser();
        analyser.fftSize = 2048;

        Tone.Destination.connect(analyser);
        animateWaveform();
    }

    function animateWaveform() {
        if (!waveformCanvas || !analyser) return;

        const ctx = waveformCanvas.getContext("2d");
        const bufferLength = analyser.fftSize;
        const dataArray = new Uint8Array(bufferLength);

        function draw() {
            requestAnimationFrame(draw);
            analyser.getByteTimeDomainData(dataArray);

            ctx.fillStyle = "#f2f2f2";
            ctx.fillRect(0, 0, waveformCanvas.width, waveformCanvas.height);

            ctx.lineWidth = 2;
            ctx.strokeStyle = "#007BFF";
            ctx.beginPath();

            const sliceWidth = waveformCanvas.width / bufferLength;
            let x = 0;

            for (let i = 0; i < bufferLength; i++) {
                const v = dataArray[i] / 128.0;
                const y = v * waveformCanvas.height / 2;

                if (i === 0) {
                    ctx.moveTo(x, y);
                } else {
                    ctx.lineTo(x, y);
                }

                x += sliceWidth;
            }

            ctx.lineTo(waveformCanvas.width, waveformCanvas.height / 2);
            ctx.stroke();
        }

        draw();
    }

    return {
        playToken,
        preload,
        stop,
        setupWaveform,
        // No-op for backward compat. The legacy bootstrap calls
        // AudioEngine.initSampler() on every page load; the new flow
        // builds the sampler lazily on first legacy playback (see
        // __initLegacySampler) and the token flow doesn't need it at
        // all. Keeping this method as a no-op avoids a TypeError on
        // page init for callers we haven't migrated.
        initSampler: () => {},
        // === Legacy API ===
        // Used only by the sheet-music exercises (IntervalMelodico,
        // and SolfegeMelody if it grows playback) where the note name
        // is already visible to the learner. Backed by Tone.Sampler
        // pulling from the authenticated /audio/{name} endpoint.
        playNote: legacyPlayNote,
        playSequence: legacyPlaySequence,
        playChord: legacyPlayChord,
        playMelodyWithRhythm: legacyPlayMelodyWithRhythm
    };
})();

// === Legacy Sampler-based API (sheet-music exercises only) ===
// Kept on AudioEngine as a separate path so the new token flow stays
// the only audio code path for the seven anti-cheat exercises.
let __legacySampler = null;
function __initLegacySampler() {
    if (__legacySampler) return __legacySampler;
    const baseUrl = "/audio/";
    const noteUrls = {};
    const notes = ["C", "D", "E", "F", "G", "A", "B"];
    const octaves = [1, 2, 3, 4, 5, 6, 7];
    for (const oct of octaves) {
        for (const n of notes) {
            const name = n + oct;
            noteUrls[name] = name + ".mp3";
            if (n !== "E" && n !== "B") {
                noteUrls[n + "#" + oct] = n + "s" + oct + ".mp3";
            }
        }
    }
    __legacySampler = new Tone.Sampler(noteUrls, { release: 1, baseUrl }).toDestination();
    return __legacySampler;
}
function legacyPlayNote(note, duration = "0.5") {
    const s = __initLegacySampler();
    s.triggerAttackRelease(note, duration);
}
function legacyPlaySequence(notes, duration = 0.5, delay = 0.6) {
    const s = __initLegacySampler();
    let t = 0;
    for (const note of notes) {
        Tone.Transport.scheduleOnce(() => s.triggerAttackRelease(note, duration), `+${t}`);
        t += delay;
    }
    Tone.Transport.start();
}
function legacyPlayChord(notes, duration = 1) {
    if (!notes || notes.length === 0) return;
    const s = __initLegacySampler();
    notes.forEach(note => s.triggerAttackRelease(note, duration));
}
function legacyPlayMelodyWithRhythm(melody) {
    if (!melody || melody.length === 0) return;
    const s = __initLegacySampler();
    let t = 0;
    for (const item of melody) {
        if (item.type === "note") {
            Tone.Transport.scheduleOnce(() => s.triggerAttackRelease(item.note, item.duration), `+${t}`);
        }
        t += item.duration;
    }
    Tone.Transport.start();
}
