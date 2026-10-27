window.audioPlayer = {
    play: function (element, dotNetRef) {
        if (element) {
            element.play().catch(function (e) {
                if (e.name !== 'AbortError') {
                    console.error('audioPlayer.play error:', e);
                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnPlaybackError', e.message || e.name || 'Unknown error');
                    }
                }
            });
        }
    },
    pause: function (element) {
        if (element) {
            element.pause();
        }
    },
    // Switches the element to a new track. Explicitly aborts whatever the previous track's
    // request was doing (pause + drop src + load) before starting the next one, rather than just
    // overwriting `src` - on Firefox, overwriting `src` while a previous fetch is still in flight
    // doesn't reliably cancel that fetch. Skipping tracks rapidly used to leave several orphaned
    // in-flight requests to the same origin piled up, which could exhaust Firefox's per-origin
    // connection limit and hang every subsequent (uncached) track until a full page reload.
    loadTrack: function (element, url) {
        if (!element) {
            return;
        }

        element.pause();
        element.removeAttribute('src');
        element.load();
        element.src = url;
    },
    getCurrentTime: function (element) {
        return element ? element.currentTime : 0;
    },
    getDuration: function (element) {
        return element ? element.duration || 0 : 0;
    },
    setCurrentTime: function (element, time) {
        if (element) {
            element.currentTime = time;
        }
    },
    setVolume: function (element, volume) {
        if (element) {
            element.volume = Math.max(0, Math.min(1, volume));
        }
    },
    getVolume: function (element) {
        return element ? element.volume : 1;
    },
    setMediaSessionActionHandlers: function (dotNetRef) {
        if (!('mediaSession' in navigator)) {
            return;
        }

        navigator.mediaSession.setActionHandler('play', () => dotNetRef.invokeMethodAsync('MediaSessionPlay'));
        navigator.mediaSession.setActionHandler('pause', () => dotNetRef.invokeMethodAsync('MediaSessionPause'));
        navigator.mediaSession.setActionHandler('previoustrack', () => dotNetRef.invokeMethodAsync('MediaSessionPrevious'));
        navigator.mediaSession.setActionHandler('nexttrack', () => dotNetRef.invokeMethodAsync('MediaSessionNext'));

        try {
            navigator.mediaSession.setActionHandler('seekto', (details) => {
                if (details.seekTime !== undefined && details.seekTime !== null) {
                    dotNetRef.invokeMethodAsync('MediaSessionSeek', details.seekTime);
                }
            });
        } catch (e) {
            // seekto not supported in this browser
        }
    },
    setMediaSessionMetadata: function (title, artist, album, artworkUrl) {
        if (!('mediaSession' in navigator)) {
            return;
        }

        var absoluteArtworkUrl = artworkUrl ? new URL(artworkUrl, document.baseURI).href : null;

        navigator.mediaSession.metadata = new MediaMetadata({
            title: title || '',
            artist: artist || '',
            album: album || '',
            artwork: absoluteArtworkUrl
                ? [96, 128, 192, 256, 384, 512].map((size) => ({
                    src: absoluteArtworkUrl,
                    sizes: `${size}x${size}`,
                    type: 'image/jpeg'
                }))
                : []
        });
    },
    setMediaSessionPlaybackState: function (state) {
        if ('mediaSession' in navigator) {
            navigator.mediaSession.playbackState = state;
        }
    },
    setMediaSessionPositionState: function (duration, position, playbackRate) {
        if (!('mediaSession' in navigator) || !('setPositionState' in navigator.mediaSession)) {
            return;
        }

        try {
            if (duration && isFinite(duration) && duration > 0 && position >= 0 && position <= duration) {
                navigator.mediaSession.setPositionState({
                    duration: duration,
                    playbackRate: playbackRate || 1,
                    position: position
                });
            }
        } catch (e) {
            // ignore invalid position state (e.g. transient mismatched values)
        }
    }
};

window.seekbar = {
    getRect: (el) => {
        if (!el || !el.getBoundingClientRect) {
            return { left: 0, width: 1 };
        }
        return el.getBoundingClientRect();
    }
};