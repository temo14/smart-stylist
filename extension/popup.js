const result = document.getElementById('result');
const saveButton = document.getElementById('save');

function line(text, className) {
  const paragraph = document.createElement('p');
  paragraph.textContent = text;
  if (className) {
    paragraph.className = className;
  }

  return paragraph;
}

function money(amount, currency) {
  try {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(amount);
  } catch {
    return `${amount} ${currency}`;
  }
}

function priceLine(capture) {
  const price = line(money(capture.price, capture.currency), 'price');
  if (capture.listPrice) {
    const was = document.createElement('span');
    was.className = 'was';
    was.textContent = money(capture.listPrice, capture.currency);
    price.append(was);
  }

  return price;
}

function historyLine(capture) {
  if (capture.daysTracked < 2) {
    return line('First day tracked, so there is no history to compare yet.', 'muted');
  }

  const days = `${capture.daysTracked} days tracked`;
  return capture.price <= capture.lowestPrice
    ? line(`Lowest price so far (${days}).`, 'good')
    : line(`Lowest seen: ${money(capture.lowestPrice, capture.currency)} (${days}).`, 'warn');
}

function sizesList(sizes) {
  // One size can appear once per colour; it counts as available if any colour has it.
  const available = new Map();
  for (const size of sizes) {
    available.set(size.label, available.get(size.label) || size.inStock !== false);
  }

  const list = document.createElement('ul');
  for (const [label, inStock] of available) {
    const item = document.createElement('li');
    item.textContent = label;
    if (!inStock) {
      item.className = 'out';
    }

    list.append(item);
  }

  return list;
}

function show(capture) {
  result.replaceChildren();
  switch (capture?.status) {
    case 'Saved': {
      const title = document.createElement('h1');
      title.textContent = capture.title;
      result.append(title, line(capture.shop, 'muted'), priceLine(capture), historyLine(capture));
      if (capture.sizes?.length) {
        result.append(sizesList(capture.sizes));
      }

      break;
    }
    case 'NoPrice':
      result.append(line(`This ${capture.shop} page has no price data, so nothing was saved.`));
      break;
    case 'UnknownShop':
      result.append(line('This shop is not on your list, so nothing was saved.'));
      break;
    case 'Offline':
      result.append(line(capture.detail ?? 'The Smart Stylist app is not running, so nothing was saved.'),
        line('Start it with: dotnet run --project src/SmartStylist.Web', 'muted'));
      break;
    default:
      result.append(line('Nothing saved on this page. Open a product page on one of your shops.'));
  }
}

async function activeTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  return tab;
}

async function refresh() {
  const tab = await activeTab();
  const key = `tab-${tab.id}`;
  const stored = await chrome.storage.session.get(key);
  show(stored[key]?.url === tab.url ? stored[key] : undefined);
}

saveButton.addEventListener('click', async () => {
  const tab = await activeTab();
  try {
    await chrome.tabs.sendMessage(tab.id, { type: 'capture-now' });
  } catch {
    show(undefined);
  }
});

chrome.storage.session.onChanged.addListener(refresh);
refresh();
