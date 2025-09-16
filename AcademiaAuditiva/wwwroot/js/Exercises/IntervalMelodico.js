document.addEventListener("DOMContentLoaded", () => {
    // Initialise Audio
    AcademiaAuditiva.init();
    AudioEngine.setupWaveform();

    // Initialise Variables
    const exerciseIdInput = document.getElementById("exerciseId");
    const exerciseId = exerciseIdInput ? exerciseIdInput.value : null;
    
    const firstDegreeSelect = document.getElementById("firstDegreeSelect");
    const lastDegreeSelect = document.getElementById("lastDegreeSelect");
    const startIntervalSelect = document.getElementById("startIntervalSelect");
    const endIntervalSelect = document.getElementById("endIntervalSelect");

    let currentMelody = null;
    let exerciseData = null;
    const exerciseStartTime = Date.now();

    // Get localizer data
    const localizer = document.getElementById("localizer");
    const incompleteTitle = localizer?.getAttribute("data-incomplete-title") || "Incompleto";
    const incompleteText = localizer?.getAttribute("data-incomplete-text") || "Por favor, responda todas as perguntas.";
    const correctMessage = localizer?.getAttribute("data-correct-message") || "Correto!";
    const correctMessageText = localizer?.getAttribute("data-correct-message-text") || "Você acertou!";
    const wrongMessage = localizer?.getAttribute("data-wrong-message") || "Incorreto!";
    const wrongMessageText = localizer?.getAttribute("data-wrong-message-text") || "Tente novamente.";

    // Play button event
    const playBtn = document.getElementById("Play");
    if (playBtn) {
        playBtn.addEventListener("click", () => {
            if (!exerciseId) return;

            // Get filter values
            const keySelect = document.querySelector('[name="keySelect"]');
            const scaleTypeSelect = document.querySelector('[name="scaleTypeSelect"]');
            
            const filters = {
                keySelect: keySelect ? keySelect.value : "C",
                scaleTypeSelect: scaleTypeSelect ? scaleTypeSelect.value : "major"
            };

            fetch("/Exercise/RequestPlay", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    exerciseId: exerciseId,
                    filters: filters
                })
            })
            .then(resp => resp.json())
            .then(data => {
                if (data.error) {
                    Swal.fire({
                        icon: "error",
                        title: "Erro",
                        text: data.error
                    });
                    return;
                }

                exerciseData = data;
                currentMelody = data.melody;
                
                // Play melody
                if (currentMelody && currentMelody.length > 0) {
                    AudioEngine.playSequence(currentMelody, 0.8, 0.6);
                }

                // Display sheet music with VexFlow if available
                displaySheetMusic(currentMelody);
                
                // Reset form
                resetSelections();
            })
            .catch(error => {
                console.error("Error:", error);
                Swal.fire({
                    icon: "error",
                    title: "Erro",
                    text: "Erro ao carregar exercício."
                });
            });
        });
    }

    // Replay button event
    const replayBtn = document.getElementById("Replay");
    if (replayBtn) {
        replayBtn.addEventListener("click", () => {
            if (!currentMelody || currentMelody.length === 0) {
                Swal.fire({
                    icon: "warning",
                    title: "Nenhuma melodia carregada",
                    text: "Clique em 'Tocar' primeiro para gerar uma melodia."
                });
                return;
            }
            
            AudioEngine.playSequence(currentMelody, 0.8, 0.6);
        });
    }

    // Validate button event
    const validateBtn = document.getElementById("validateGuess");
    if (validateBtn) {
        validateBtn.addEventListener("click", () => {
            if (!exerciseData) {
                Swal.fire({
                    icon: "warning",
                    title: "Nenhuma melodia carregada",
                    text: "Clique em 'Tocar' primeiro para gerar uma melodia."
                });
                return;
            }

            // Get user answers
            const firstDegree = firstDegreeSelect.value;
            const lastDegree = lastDegreeSelect.value;
            const startInterval = startIntervalSelect.value;
            const endInterval = endIntervalSelect.value;

            // Validate all fields are filled
            if (!firstDegree || !lastDegree || !startInterval || !endInterval) {
                Swal.fire({
                    icon: "warning",
                    title: incompleteTitle,
                    text: incompleteText
                });
                return;
            }

            // Prepare user guess in format: "firstDegree|lastDegree|startInterval|endInterval"
            const userGuess = `${firstDegree}|${lastDegree}|${startInterval}|${endInterval}`;

            fetch("/Exercise/ValidateExercise", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    ExerciseId: exerciseId,
                    UserGuess: userGuess,
                    TimeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
                })
            })
            .then(resp => resp.json())
            .then(data => {
                const correctCountEl = document.getElementById("correctCount");
                const errorCountEl = document.getElementById("errorCount");
                
                if (data.isCorrect) {
                    if (correctCountEl) {
                        correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
                    }
                    Swal.fire({
                        icon: "success",
                        title: correctMessage,
                        text: correctMessageText
                    });
                } else {
                    if (errorCountEl) {
                        errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
                    }
                    
                    // Parse the correct answer
                    const correctAnswers = data.answer.split('|');
                    const correctText = correctAnswers.length >= 4 ? 
                        `Primeiro grau: ${correctAnswers[0]}, Último grau: ${correctAnswers[1]}, Intervalo início: ${correctAnswers[2]}, Intervalo fim: ${correctAnswers[3]}` :
                        data.answer;
                    
                    Swal.fire({
                        icon: "error",
                        title: wrongMessage,
                        text: `${wrongMessageText} Resposta correta: ${correctText}`
                    });
                }

                // Reset form after validation
                resetSelections();
                currentMelody = null;
                exerciseData = null;
            })
            .catch(error => {
                console.error("Error:", error);
                Swal.fire({
                    icon: "error",
                    title: "Erro",
                    text: "Erro ao validar resposta."
                });
            });
        });
    }

    // Helper functions
    function resetSelections() {
        if (firstDegreeSelect) firstDegreeSelect.value = "";
        if (lastDegreeSelect) lastDegreeSelect.value = "";
        if (startIntervalSelect) startIntervalSelect.value = "";
        if (endIntervalSelect) endIntervalSelect.value = "";
    }

    function displaySheetMusic(melody) {
        const outputElement = document.getElementById("output-sheet");
        if (!outputElement || !melody || !window.Vex) return;

        try {
            // Clear previous content
            outputElement.innerHTML = "";
            
            const VF = Vex.Flow;
            const renderer = new VF.Renderer(outputElement, VF.Renderer.Backends.SVG);
            renderer.resize(500, 120);
            const context = renderer.getContext();
            
            const stave = new VF.Stave(10, 10, 480);
            stave.addClef("treble").setContext(context).draw();
            
            const notes = melody.map((note, index) => {
                // Convert note to VexFlow format
                const vexNote = convertToVexFlowNote(note);
                return new VF.StaveNote({
                    clef: "treble",
                    keys: [vexNote],
                    duration: "q"
                });
            });
            
            if (notes.length > 0) {
                const voice = new VF.Voice({ num_beats: notes.length, beat_value: 4 });
                voice.addTickables(notes);
                
                const formatter = new VF.Formatter().joinVoices([voice]).format([voice], 400);
                voice.draw(context, stave);
            }
        } catch (error) {
            console.error("Error displaying sheet music:", error);
        }
    }

    function convertToVexFlowNote(note) {
        // Convert note format (e.g., "C4", "F#4") to VexFlow format (e.g., "c/4", "f#/4")
        if (!note || note === "rest") return "b/4"; // Default to B4 for invalid notes
        
        const noteName = note.replace(/\d/, "").toLowerCase();
        const octave = note.match(/\d/)?.[0] || "4";
        
        return `${noteName}/${octave}`;
    }
});