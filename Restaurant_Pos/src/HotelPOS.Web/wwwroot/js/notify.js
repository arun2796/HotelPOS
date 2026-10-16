// Attention signal for the Blazor client (Phase 20.2): a short beep through the Web Audio API and a vibration where the
// device has one. Browsers only allow audio after the user has touched the page once, so every call is wrapped in
// try/catch and a blocked or missing audio device is simply silent.
window.hotelposNotify = (function () {
    var context = null;

    function beep() {
        try {
            var Ctor = window.AudioContext || window.webkitAudioContext;
            if (!Ctor) { return false; }
            context = context || new Ctor();
            if (context.state === 'suspended') { context.resume(); }
            var oscillator = context.createOscillator();
            var gain = context.createGain();
            oscillator.type = 'sine';
            oscillator.frequency.value = 880;
            gain.gain.setValueAtTime(0.2, context.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.001, context.currentTime + 0.25);
            oscillator.connect(gain);
            gain.connect(context.destination);
            oscillator.start();
            oscillator.stop(context.currentTime + 0.25);
            return true;
        } catch { return false; }
    }

    function vibrate() {
        try { return typeof navigator.vibrate === 'function' && navigator.vibrate([120, 60, 120]); } catch { return false; }
    }

    return {
        beep: beep,
        vibrate: vibrate,
        signal: function () { var played = beep(); var shook = vibrate(); return played || shook; }
    };
})();
