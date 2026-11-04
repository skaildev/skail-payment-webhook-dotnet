const LAST_KEY = 'payment-webhook.last';
const SESSION = Math.random().toString(36).slice(2, 6);   // keeps event ids of this browser session apart
const LABELS = { NotAccepted: 'Not accepted', Received: 'Received', Running: 'Running', Released: 'Released', Pending: 'Pending', Failed: 'Failed' };

const $ = (id) => document.getElementById(id);
const els = {
  scenarios: $('scenarios'), again: $('again'), reset: $('reset'), sent: $('sent'), error: $('error'),
  invoices: $('invoices'), events: $('events'), eventsEmpty: $('eventsEmpty'),
  reviews: $('reviews'), reviewsEmpty: $('reviewsEmpty'),
};

let counter = 0;
let last = readLast();

function readLast() {
  try { return JSON.parse(localStorage.getItem(LAST_KEY)); } catch { return null; }
}

function saveLast(payment) {
  last = payment;
  try { localStorage.setItem(LAST_KEY, JSON.stringify(payment)); } catch { /* storage unavailable */ }
  els.again.disabled = false;
}

function showError(message) {
  els.error.hidden = !message;
  els.error.textContent = message ?? '';
}

// text only (the event id is free text): never build HTML from it
function cell(row, text, className) {
  const td = row.insertCell();
  td.textContent = text;
  if (className) td.className = className;
  return td;
}

function badge(state) {
  const span = document.createElement('span');
  span.className = `badge ${state}`;
  span.textContent = LABELS[state] ?? state;
  return span;
}

function money(value) {
  return 'R$ ' + Number(value).toFixed(2);
}

function render(state) {
  els.invoices.replaceChildren();
  for (const i of state.invoices) {
    const row = els.invoices.insertRow();
    cell(row, i.orderId);
    cell(row, money(i.amount));
    cell(row, i.paidByEvent ? `paid by ${i.paidByEvent}` : 'open');
    cell(row, `×${i.releases}`, i.releases > 1 ? 'dup' : '');
  }

  els.events.replaceChildren();
  for (const e of [...state.events].reverse()) {
    const row = els.events.insertRow();
    cell(row, e.eventId, 'mono');
    cell(row, e.orderId);
    cell(row, money(e.amount));
    cell(row, e.taskId.slice(0, 8), 'mono').title = e.taskId;
    cell(row, `×${e.deliveries}`, e.deliveries > 1 ? 'dup' : '');
    const status = row.insertCell();
    status.append(badge(e.state));
    if (e.result) {
      const result = document.createElement('span');
      result.className = 'result';
      result.textContent = e.result;
      status.append(result);
    }
  }
  els.eventsEmpty.hidden = state.events.length > 0;

  els.reviews.replaceChildren();
  for (const p of state.reviews) {
    const row = els.reviews.insertRow();
    cell(row, p.eventId, 'mono');
    cell(row, p.orderId);
    cell(row, p.reason);
  }
  els.reviewsEmpty.hidden = state.reviews.length > 0;
}

// the gateway simulator calls the real webhook endpoint, like the payment gateway would
async function send(payment) {
  showError(null);
  saveLast(payment);
  els.sent.textContent = `sent ${payment.eventId}…`;
  try {
    const response = await fetch('/webhooks/payments', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payment),
    });
    const body = await response.json().catch(() => ({}));
    els.sent.textContent = `${payment.eventId} → ${response.status}` + (body.taskId ? ` · execution ${body.taskId.slice(0, 8)}` : '');
    if (!response.ok) showError(body.error ?? `the webhook answered ${response.status}`);
  } catch (e) {
    els.sent.textContent = '';
    showError(`could not reach the webhook: ${e.message}`);
  }
}

els.scenarios.addEventListener('click', (event) => {
  const button = event.target.closest('button[data-order]');
  if (!button) return;
  counter += 1;
  send({ eventId: `evt_${SESSION}_${counter}`, orderId: button.dataset.order, amount: Number(button.dataset.amount) });
});

els.again.addEventListener('click', () => { if (last) send(last); });

els.reset.addEventListener('click', async () => {
  showError(null);
  await fetch('/api/reset', { method: 'POST' });
  els.sent.textContent = '';
});

// the snapshot comes first and again on every change; EventSource reconnects by itself
els.again.disabled = !last;
const source = new EventSource('/api/stream');
source.onmessage = (message) => render(JSON.parse(message.data));
source.onerror = () => { /* the browser retries; the last snapshot stays on screen */ };
