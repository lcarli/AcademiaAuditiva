
const Feedback = (() => {
    function playSuccessSound(title, message) {
        Swal.fire({
            icon: 'success',
            title: title,
            text: message
        });
    }

    function playErrorSound(title, message, answer) {
        Swal.fire({
            icon: 'error',
            title: title,
            text: message + " " + answer.replace('s', '#'),
        });
    }

    return {
        playSuccessSound,
        playErrorSound
    };
})();
