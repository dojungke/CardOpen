mergeInto(LibraryManager.library, {
  CardOpenShareResult: function (titlePtr, textPtr, urlPtr) {
    var title = UTF8ToString(titlePtr);
    var text = UTF8ToString(textPtr);
    var url = UTF8ToString(urlPtr);

    if (navigator.share) {
      navigator.share({ title: title, text: text, url: url }).catch(function () {});
      return;
    }

    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(url).catch(function () {
        window.prompt('Copy result link', url);
      });
      return;
    }

    window.prompt('Copy result link', url);
  }
});
