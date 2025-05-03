document.addEventListener("DOMContentLoaded", () => {
  // Initialize VexFlow
  const VF = Vex.Flow;
  const div = document.getElementById("output-sheet");
  const renderer = new VF.Renderer(div, VF.Renderer.Backends.SVG);
  renderer.resize(500, 150);
  const context = renderer.getContext();
  const stave = new VF.Stave(10, 40, 480);
  stave.addClef("treble").setContext(context).draw();

  let mediaRecorder;
  let audioChunks = [];
  let isRecording = false;
  let recordedAudioUrl = null;
  const loc = document.getElementById("localizer").dataset;
  const exerciseId = document.getElementById("exerciseId")?.value;

  // Function to render the melody on the sheet
  function renderMelody(notes) {
    context.clear();

    // Recria a pauta (stave) do zero para evitar duplicações de clave e compasso
    const newStave = new VF.Stave(10, 40, 480);
    newStave.addClef("treble").addTimeSignature("4/4").setContext(context).draw();

    const durationMap = {
      4.0: "w", // whole note
      2.0: "h", // half note
      1.0: "q", // quarter note
      0.5: "8", // eighth note
      0.25: "16", // sixteenth note
    };

    const vexNotes = notes.map((note) => {
      if (note.type === "rest") {
        return new VF.StaveNote({
          clef: "treble",
          keys: ["b/4"],
          duration: durationMap[note.duration] + "r",
        });
      } else {
        return new VF.StaveNote({
          clef: "treble",
          keys: [note.note.toLowerCase().replace(/(\d)/, "/$1")],
          duration: durationMap[note.duration] || "q",
        });
      }
    });

    const voice = new VF.Voice({ num_beats: 4, beat_value: 4 });
    voice.addTickables(vexNotes);

    new VF.Formatter().joinVoices([voice]).format([voice], 400);
    voice.draw(context, newStave);
  }

  // Play button functionality
  const playBtn = document.getElementById("Generate");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      fetch(`/Exercise/RequestPlay`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ exerciseId }),
      })
        .then((response) => response.json())
        .then((data) => {
          renderMelody(data.melody);
        })
        .catch((err) => console.error("Error fetching melody:", err));
    });
  }

  // Open microphone and start recording
  // Gravar ou parar a gravação
  const recordBtn = document.getElementById("recordAudio");
  if (recordBtn) {
    recordBtn.addEventListener("click", async () => {
      if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        alert("Microphone access is not supported in this browser.");
        return;
      }

      if (!isRecording) {
        // Iniciar gravação
        try {
          const stream = await navigator.mediaDevices.getUserMedia({
            audio: true,
          });
          mediaRecorder = new MediaRecorder(stream);

          audioChunks = [];
          mediaRecorder.ondataavailable = (event) => {
            audioChunks.push(event.data);
          };

          mediaRecorder.onstop = () => {
            const audioBlob = new Blob(audioChunks, { type: "audio/wav" });
            recordedAudioUrl = URL.createObjectURL(audioBlob);
          };

          mediaRecorder.start();
          isRecording = true;
          recordBtn.innerHTML = `<i class="bi bi-stop-circle"></i> Parar Gravação`;
        } catch (err) {
          console.error("Error accessing microphone:", err);
        }
      } else {
        // Parar gravação
        if (mediaRecorder && mediaRecorder.state === "recording") {
          mediaRecorder.stop();
          isRecording = false;
          recordBtn.innerHTML = `<i class="bi bi-mic"></i> Gravar Áudio`;
        }
      }
    });
  }

  // Escutar áudio gravado
  const listenBtn = document.getElementById("listenAudio");
  if (listenBtn) {
    listenBtn.addEventListener("click", () => {
      if (recordedAudioUrl) {
        const audio = new Audio(recordedAudioUrl);
        audio.play();
      } else {
        alert("Nenhum áudio gravado para reproduzir.");
      }
    });
  }

  // Validar e enviar áudio gravado
  const validateBtn = document.getElementById("validateGuess");
  if (validateBtn) {
    validateBtn.addEventListener("click", () => {
      if (recordedAudioUrl) {
        fetch(recordedAudioUrl)
          .then((response) => response.blob())
          .then((audioBlob) => {
            sendAudioToBackend(audioBlob);
          })
          .catch((err) => console.error("Error fetching recorded audio:", err));
      } else {
        alert("Nenhum áudio gravado para validar.");
      }
    });
  }

  // Send recorded audio to the backend
  async function sendAudioToBackend(audioBlob) {
    try {
      const note = await analyzeAudio(audioBlob);
      alert(`Nota identificada: ${note}`);

      if (note) {
        // Enviar apenas a nota identificada
        fetch(`/Exercise/SubmitNote`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ exerciseId, note }),
        });
      } else {
        // Fallback: enviar o áudio completo
        const formData = new FormData();
        formData.append("exerciseId", exerciseId);
        formData.append("audio", audioBlob);

        fetch(`/Exercise/SubmitAudio`, {
          method: "POST",
          body: formData,
        });
      }
    } catch (err) {
      console.error("Error analyzing or submitting audio:", err);
    }
  }

  function frequencyToNoteName(frequency) {
    const noteNames = [
      "C",
      "C#",
      "D",
      "D#",
      "E",
      "F",
      "F#",
      "G",
      "G#",
      "A",
      "A#",
      "B",
    ];
    const A4 = 440; // Frequência do A4
    const semitoneRatio = Math.pow(2, 1 / 12);

    const noteNumber = Math.round(12 * Math.log2(frequency / A4)) + 69;
    const octave = Math.floor(noteNumber / 12) - 1;
    const noteIndex = ((noteNumber % 12) + 12) % 12;

    return `${noteNames[noteIndex]}${octave}`;
  }

  async function analyzeAudio(audioBlob) {

    const audioContext = new AudioContext();
    const arrayBuffer = await audioBlob.arrayBuffer();
    const decodedAudio = await audioContext.decodeAudioData(arrayBuffer);
    const channelData = decodedAudio.getChannelData(0);

    const { EssentiaWASM } = await import('../dist/essentia-wasm.es.js');
    const essentia = new Essentia(EssentiaWASM);



    const inputSignalVector = essentia.arrayToVector(channelData);
    let outputRG = essentia.ReplayGain(inputSignalVector, 44100);


    let outputPyYin = essentia.PitchYinProbabilistic(inputSignalVector,
      4096, // frameSize
      256, // hopSize
      0.01, // lowRMSThreshold
      'zero', // outputUnvoiced,
      false, // preciseTime
      44100); //sampleRate

    let pitches = essentia.vectorToArray(outputPyYin.pitch);
    let voicedProbabilities = essentia.vectorToArray(outputPyYin.voicedProbabilities);


    const voicedThreshold = 0.6;
    
    const minLength = Math.min(pitches.length, voicedProbabilities.length);
    const filtered = [];
    
    for (let i = 0; i < minLength; i++) {
      const pitch = pitches[i];
      const prob = voicedProbabilities[i];
    
      if (prob >= 0.6 && pitch > 0) {
        filtered.push({ pitch, prob });
      }
    }

    if (filtered.length === 0) return null;

    const avgPitch =
      filtered.reduce((sum, p) => sum + p.pitch, 0) / filtered.length;

    const noteName = frequencyToNoteName(avgPitch);


    // CAUTION: only use the `shutdown` and `delete` methods below if you've finished your analysis and don't plan on re-using Essentia again in your program lifecycle.

    // call internal essentia::shutdown C++ method
    //essentia.shutdown();
    // delete EssentiaJS instance, free JS memory
    //essentia.delete();
    return noteName; 
  }
});
