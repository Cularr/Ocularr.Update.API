'use strict';

(function () {
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
