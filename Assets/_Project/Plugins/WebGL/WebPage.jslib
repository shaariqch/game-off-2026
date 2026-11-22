// Calls from the game into the page (Assets/WebGLTemplates/FullWindow).
mergeInto(LibraryManager.library, {
  // The browser's own pointer lock state. Unity's Cursor.lockState on WebGL reports what was asked for, not whether
  // the browser granted it, so GameInput asks here.
  WebPage_IsPointerLocked: function () {
    return document.pointerLockElement ? 1 : 0;
  },

  // Only works shortly after a click or key press (the browser's user activation), so GameInput calls it right after
  // a Start or Continue click. A refused request is fine: the next click asks again.
  WebPage_RequestPointerLock: function () {
    if (window.webPageLog) window.webPageLog("unity requests pointer lock");
    try {
      var canvas = Module.canvas || document.querySelector("canvas");
      if (!canvas || document.pointerLockElement === canvas) return;
      // the page template adds raw movement to every request (see its comment)
      var request = canvas.requestPointerLock();
      if (request && request.catch) request.catch(function () {});
    } catch (e) {}
  },

  // a line for the local test log (Tools/webdev_server.py); does nothing elsewhere
  WebPage_Log: function (text) {
    if (window.webPageLog) window.webPageLog("unity " + UTF8ToString(text));
  },
});
