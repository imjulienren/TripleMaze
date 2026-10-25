mergeInto(LibraryManager.library, {
  TripleMazePulse: function (duration) {
    if (typeof navigator !== 'undefined' && typeof navigator.vibrate === 'function') {
      try { navigator.vibrate(duration); } catch (e) { }
    }
  }
});
