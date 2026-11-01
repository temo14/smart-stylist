// Runs on shop pages. Sends only the page's structured data, never the rest of the page,
// so account names and order details stay in the browser.
const STRUCTURED_DATA = [
  'link[rel="canonical"]',
  'script[type="application/ld+json"]',
  '[itemtype*="schema.org/Product"]',
  'meta[property^="og:"]',
  'meta[property^="product:"]',
].join(',');

// Shops that build the page with JavaScript add their data late, and change the URL without a reload.
const FIRST_LOOK_MS = 2000;
const LAST_LOOK_MS = 6000;

function structuredData() {
  return [...document.querySelectorAll(STRUCTURED_DATA)].map(element => element.outerHTML).join('\n');
}

function hasProductData(html) {
  return /ld\+json|schema\.org\/Product|product:price|og:price/.test(html);
}

function capture() {
  const html = structuredData();
  if (hasProductData(html)) {
    chrome.runtime.sendMessage({ type: 'capture', url: location.href, html });
  }

  return hasProductData(html);
}

let lastUrl = '';

function watchUrl() {
  if (location.href === lastUrl) {
    return;
  }

  lastUrl = location.href;
  const url = lastUrl;
  setTimeout(() => {
    if (location.href === url && !capture()) {
      setTimeout(() => location.href === url && capture(), LAST_LOOK_MS - FIRST_LOOK_MS);
    }
  }, FIRST_LOOK_MS);
}

watchUrl();
setInterval(watchUrl, 1000);

chrome.runtime.onMessage.addListener((message, _sender, respond) => {
  if (message.type === 'capture-now') {
    respond({ found: capture() });
  }
});
