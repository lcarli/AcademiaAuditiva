
const AcademiaAuditiva = {
    init: () => {
        AudioEngine.initSampler();
        console.log("Academia Auditiva pronta!");
    },
    games: GameEngine,
    audio: AudioEngine
};
