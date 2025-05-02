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
    stave.setContext(context).draw();

    // Adiciona a indicação de compasso 4/4
    stave.addTimeSignature("4/4").draw();

    const vexNotes = notes.map((note) => {
      const durationMap = {
        4.0: "w", // whole note
        2.0: "h", // half note
        1.0: "q", // quarter note
        0.5: "8", // eighth note
        0.25: "16", // sixteenth note
      };

      if (note.type === "rest") {
        // Create a rest
        return new VF.StaveNote({
          clef: "treble",
          keys: ["b/4"], // Default key for rests
          duration: durationMap[note.duration] + "r", // Add "r" for rest
        });
      } else {
        // Create a note
        return new VF.StaveNote({
          clef: "treble",
          keys: [note.note.toLowerCase().replace(/(\d)/, "/$1")], // Convert "C4" to "c/4"
          duration: durationMap[note.duration] || "q", // Default to quarter note if duration is missing
        });
      }
    });

    const voice = new VF.Voice({ num_beats: 4, beat_value: 4 });
    voice.addTickables(vexNotes);

    const formatter = new VF.Formatter()
      .joinVoices([voice])
      .format([voice], 400);
    voice.draw(context, stave);
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
            sendAudioToBackend(audioBlob);
          };

          mediaRecorder.start();
          isRecording = true;
          recordBtn.innerHTML = `<i class="bi bi-stop-circle"></i> Parar Gravação`;
          alert("Gravação iniciada. Clique novamente para parar.");
        } catch (err) {
          console.error("Error accessing microphone:", err);
        }
      } else {
        // Parar gravação
        if (mediaRecorder && mediaRecorder.state === "recording") {
          mediaRecorder.stop();
          isRecording = false;
          recordBtn.innerHTML = `<i class="bi bi-mic"></i> Gravar Áudio`;
          alert("Gravação finalizada.");
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
  function sendAudioToBackend(audioBlob) {
    const formData = new FormData();
    formData.append("exerciseId", exerciseId);
    formData.append("audio", audioBlob);

    fetch(`/Exercise/SubmitAudio`, {
      method: "POST",
      body: formData,
    })
      .then((response) => response.json())
      .then((data) => {
        if (data.isCorrect) {
          Swal.fire("Correct!", loc.correctMessageText, "success");
        } else {
          Swal.fire("Wrong!", loc.wrongMessageText, "error");
        }
      })
      .catch((err) => console.error("Error submitting audio:", err));
  }
});
