const VF = Vex.Flow;
let notes = [];
let renderer, context, stave;
let selectedTimeSignature = "4/4";

window.onload = function () {
    const div = document.getElementById("staff-container");
    renderer = new VF.Renderer(div, VF.Renderer.Backends.SVG);
    renderer.resize(600, 200);
    context = renderer.getContext();
    drawStaff();
};

function drawStaff() {
    context.clear();
    stave = new VF.Stave(10, 40, 500);
    stave.addClef("treble").addTimeSignature(selectedTimeSignature);
    stave.setContext(context).draw();

    const voice = new VF.Voice({ 
        num_beats: parseInt(selectedTimeSignature.split('/')[0]), 
        beat_value: parseInt(selectedTimeSignature.split('/')[1]) 
    });
    voice.addTickables(notes);

    new VF.Formatter().joinVoices([voice]).format([voice], 400);
    voice.draw(context, stave);
}

function addNote(pitch, duration) {
    notes.push(new VF.StaveNote({ keys: [pitch.toLowerCase()], duration }));
    drawStaff();
}

function addRest(duration) {
    notes.push(new VF.StaveNote({ keys: ["b/4"], duration: duration + "r" }));
    drawStaff();
}

function changeTimeSignature(sig) {
    selectedTimeSignature = sig;
    drawStaff();
}

function clearStaff() {
    notes = [];
    drawStaff();
}

function submitMelody() {
    const payload = {
        timeSignature: selectedTimeSignature,
        notes: notes.map((n, idx) => ({
            pitch: n.isRest() ? "rest" : n.keys[0].toUpperCase(),
            duration: n.duration,
            beat: idx
        }))
    };

    fetch("/Exercise/ValidateMelody", {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify(payload)
    })
    .then(res => res.json())
    .then(data => {
        document.getElementById("result").innerHTML = data.isCorrect
            ? "<div class='alert alert-success'>Melodia correta!</div>"
            : "<div class='alert alert-danger'>Melodia incorreta.</div>";
    });
}