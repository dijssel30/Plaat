window.plaatSession = {
    establish: function (accessToken, expiresAt) {
        return fetch('/session/establish', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ accessToken: accessToken, expiresAt: expiresAt })
        });
    },
    clear: function () {
        return fetch('/session/clear', {
            method: 'POST',
            credentials: 'same-origin'
        });
    }
};
