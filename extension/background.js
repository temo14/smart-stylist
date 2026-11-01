const API = 'http://localhost:5227/api/captures';

const BADGES = {
  Saved: { text: '✓', color: '#1a7f37' },
  NoPrice: { text: '', color: '#000000' },
  UnknownShop: { text: '', color: '#000000' },
  Offline: { text: '!', color: '#cf222e' },
};

async function save(url, html) {
  try {
    const response = await fetch(API, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url, html }),
    });
    if (response.ok) {
      return await response.json();
    }

    return { status: 'Offline', detail: `The app answered ${response.status}.` };
  } catch {
    return { status: 'Offline' };
  }
}

chrome.runtime.onMessage.addListener((message, sender) => {
  if (message.type !== 'capture' || !sender.tab) {
    return;
  }

  const tabId = sender.tab.id;
  save(message.url, message.html).then(result => {
    chrome.storage.session.set({ [`tab-${tabId}`]: { ...result, url: message.url } });
    const badge = BADGES[result.status] ?? BADGES.NoPrice;
    chrome.action.setBadgeText({ tabId, text: badge.text });
    chrome.action.setBadgeBackgroundColor({ tabId, color: badge.color });
  });
});

chrome.tabs.onRemoved.addListener(tabId => chrome.storage.session.remove(`tab-${tabId}`));
