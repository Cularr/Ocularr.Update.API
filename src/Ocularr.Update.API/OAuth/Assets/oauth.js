'use strict';

(function () {
  if (window.location.search) {
    window.history.replaceState(null, '', window.location.pathname);
  }

  var button = document.getElementById('close');

  if (!button) {
    return;
  }

  button.addEventListener('click', function () {
    window.close();

    setTimeout(function () {
      window.location.replace('about:blank');
    }, 100);
  });
})();
