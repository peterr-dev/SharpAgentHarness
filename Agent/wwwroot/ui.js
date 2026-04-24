const requestDefinitions = {
  'create-session': {
    method: 'POST',
    path: '/sessions',
    fields: [
      { key: 'model', label: 'Model', type: 'text', placeholder: 'gpt-5-nano', defaultValue: 'gpt-5-nano' },
      { key: 'instructions', label: 'Instructions', type: 'textarea', placeholder: 'You are a helpful assistant.', defaultValue: 'You are a helpful assistant.' },
      {
        key: 'chatCompletionsUrl',
        label: 'Chat Completions URL',
        type: 'text',
        placeholder: 'https://api.openai.com/v1/chat/completions',
      },
      { key: 'promptCacheKey', label: 'Prompt Cache Key', type: 'text', placeholder: 'SharpAgentHarness', defaultValue: 'SharpAgentHarness' },
      {
        key: 'tier',
        label: 'Service Tier',
        type: 'select',
        options: ['Auto', 'Default', 'Flex', 'Priority'],
        defaultValue: 'Auto'
      },
      {
        key: 'reasoning',
        label: 'Reasoning Effort',
        type: 'select',
        options: ['None', 'Minimal', 'Low', 'Medium', 'High', 'XHigh'],
        defaultValue: 'Minimal'
      },
      {
        key: 'verbosity',
        label: 'Text Verbosity',
        type: 'select',
        options: ['Low', 'Medium', 'High'],
        defaultValue: 'Low'
      }
    ]
  },
  'submit-message': {
    method: 'POST',
    path: '/sessions/{sessionId}/messages',
    fields: [
      { key: 'sessionId', label: 'Session ID', type: 'text', placeholder: 'GUID', required: true },
      { key: 'message', label: 'Message', type: 'textarea', placeholder: 'Hello there', required: true }
    ]
  },
  'get-session': {
    method: 'GET',
    path: '/sessions/{sessionId}',
    fields: [{ key: 'sessionId', label: 'Session ID', type: 'text', placeholder: 'GUID', required: true }]
  },
  'get-events': {
    method: 'GET',
    path: '/sessions/{sessionId}/events',
    fields: [{ key: 'sessionId', label: 'Session ID', type: 'text', placeholder: 'GUID', required: true }]
  }
};

const baseUrlInput = document.getElementById('baseUrl');
const requestTypeSelect = document.getElementById('requestType');
const dynamicFields = document.getElementById('dynamicFields');
const sendBtn = document.getElementById('sendBtn');
const clearBtn = document.getElementById('clearBtn');
const statusPill = document.getElementById('statusPill');
const cachePill = document.getElementById('cachePill');
const responseBody = document.getElementById('responseBody');
const copyBtn = document.getElementById('copyBtn');

const openAiHostedOnlyFieldKeys = ['model', 'promptCacheKey', 'tier', 'reasoning', 'verbosity'];


// Keep track of the most recently created session so follow-up calls are quicker to fill in.
const LAST_SESSION_ID_STORAGE_KEY = 'sharpAgentHarnessLastSessionId';
let lastCreatedSessionId = loadLastSessionId();

function loadLastSessionId() {
  try {
    return localStorage.getItem(LAST_SESSION_ID_STORAGE_KEY) || '';
  } catch {
    return '';
  }
}

function saveLastSessionId(sessionId) {
  lastCreatedSessionId = sessionId;

  try {
    localStorage.setItem(LAST_SESSION_ID_STORAGE_KEY, sessionId);
  } catch {
    // Ignore storage write errors, because session auto-fill is a convenience feature.
  }
}

function findSessionIdFromResponse(responseText) {
  try {
    const parsed = JSON.parse(responseText);

    if (parsed && typeof parsed === 'object') {
      if (typeof parsed.id === 'string') return parsed.id;
      if (typeof parsed.sessionId === 'string') return parsed.sessionId;
    }
  } catch {
    return '';
  }

  return '';
}

function populateSessionIdFieldIfAvailable() {
  if (!lastCreatedSessionId) {
    return;
  }

  const sessionIdField = dynamicFields.querySelector('[data-field="sessionId"]');
  if (!sessionIdField) {
    return;
  }

  if (!sessionIdField.value.trim()) {
    sessionIdField.value = lastCreatedSessionId;
  }
}

function setStatus(label, tone = 'idle') {
  statusPill.textContent = label;
  statusPill.className = `status-pill status-${tone}`;
}

function setCacheStatus(label, tone = 'unknown') {
  cachePill.textContent = label;
  cachePill.className = `status-pill cache-pill cache-pill-${tone}`;
}

function getCachedInputTokens(responseText) {
  try {
    const parsed = JSON.parse(responseText);

    // The API has several response shapes; inspect each common shape in priority order.
    const candidates = [
      parsed?.usageTotals?.cachedInputTokens,
      parsed?.details?.session?.usageTotals?.cachedInputTokens,
      parsed?.usage?.cachedInputTokens,
      parsed?.details?.response?.usage?.cachedInputTokens
    ];

    for (const candidate of candidates) {
      if (typeof candidate === 'number' && Number.isFinite(candidate) && candidate >= 0) {
        return candidate;
      }
    }

    // Event responses return an array; use the latest value that contains cache usage.
    if (Array.isArray(parsed)) {
      for (let index = parsed.length - 1; index >= 0; index -= 1) {
        const item = parsed[index];
        const eventCachedTokens = item?.details?.session?.usageTotals?.cachedInputTokens
          ?? item?.details?.response?.usage?.cachedInputTokens;

        if (typeof eventCachedTokens === 'number' && Number.isFinite(eventCachedTokens) && eventCachedTokens >= 0) {
          return eventCachedTokens;
        }
      }
    }
  } catch {
    return null;
  }

  return null;
}

function isLocalChatCompletionsUrl(urlValue) {
  if (!urlValue) {
    return false;
  }

  const trimmedValue = urlValue.trim();

  if (/^(localhost|127\.0\.0\.1|\[::1\]|::1)(:\d+)?(\/.*)?$/i.test(trimmedValue)) {
    return true;
  }

  try {
    const parsedUrl = new URL(trimmedValue);
    const hostname = parsedUrl.hostname.toLowerCase();
    return hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1' || hostname === '[::1]';
  } catch {
    return false;
  }
}

function updateCreateSessionFieldVisibility() {
  if (requestTypeSelect.value !== 'create-session') {
    return;
  }

  const chatCompletionsUrlField = dynamicFields.querySelector('[data-field="chatCompletionsUrl"]');
  const usesLocalChatCompletionsUrl = chatCompletionsUrlField
    ? isLocalChatCompletionsUrl(chatCompletionsUrlField.value.trim())
    : false;

  openAiHostedOnlyFieldKeys.forEach((fieldKey) => {
    const row = dynamicFields.querySelector(`[data-field-row="${fieldKey}"]`);

    if (row) {
      row.classList.toggle('is-hidden', usesLocalChatCompletionsUrl);
    }
  });
}

function renderDynamicFields() {
  const definition = requestDefinitions[requestTypeSelect.value];
  dynamicFields.innerHTML = '';

  definition.fields.forEach((field) => {
    const row = document.createElement('div');
    row.className = `row ${field.type === 'textarea' ? 'row-top' : ''}`;
    row.dataset.fieldRow = field.key;

    const label = document.createElement('label');
    label.textContent = field.label;
    label.setAttribute('for', field.key);

    let control;
    if (field.type === 'textarea') {
      control = document.createElement('textarea');
    } else if (field.type === 'select') {
      control = document.createElement('select');
      field.options.forEach((optionValue) => {
        const option = document.createElement('option');
        option.value = optionValue;
        option.textContent = optionValue;
        control.appendChild(option);
      });
    } else {
      control = document.createElement('input');
      control.type = 'text';
    }

    control.id = field.key;
    control.dataset.field = field.key;
    control.placeholder = field.placeholder || '';

    if (field.defaultValue !== undefined) {
      control.value = field.defaultValue;
    }

    const inputWrapper = document.createElement('div');
    inputWrapper.appendChild(control);

    if (field.note) {
      const note = document.createElement('p');
      note.className = 'field-note';
      note.textContent = field.note;
      inputWrapper.appendChild(note);
    }

    row.appendChild(label);
    row.appendChild(inputWrapper);
    dynamicFields.appendChild(row);
  });

  const chatCompletionsUrlField = dynamicFields.querySelector('[data-field="chatCompletionsUrl"]');
  if (chatCompletionsUrlField) {
    chatCompletionsUrlField.addEventListener('input', updateCreateSessionFieldVisibility);
    chatCompletionsUrlField.addEventListener('change', updateCreateSessionFieldVisibility);
  }

  updateCreateSessionFieldVisibility();

  populateSessionIdFieldIfAvailable();
}

function readFormValues() {
  const values = {};
  dynamicFields.querySelectorAll('[data-field]').forEach((fieldElement) => {
    values[fieldElement.dataset.field] = fieldElement.value.trim();
  });
  return values;
}

function formatIfJson(text) {
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

// Central wrapper for browser API requests, so network errors are surfaced consistently.
async function callApi(url, method, payload) {
  try {
    const options = { method, headers: {} };

    if (payload !== undefined) {
      options.headers['Content-Type'] = 'application/json';
      options.body = JSON.stringify(payload);
    }

    const response = await fetch(url, options);
    const text = await response.text();

    return {
      ok: response.ok,
      status: response.status,
      statusText: response.statusText,
      contentType: response.headers.get('content-type') || 'unknown content-type',
      text
    };
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`Network/CORS failure: ${message}`);
  }
}

function buildRequest() {
  const definition = requestDefinitions[requestTypeSelect.value];
  const baseUrl = baseUrlInput.value.trim().replace(/\/$/, '');
  const values = readFormValues();

  if (!baseUrl) {
    throw new Error('Base API URL is required.');
  }

  for (const field of definition.fields) {
    if (field.required && !values[field.key]) {
      throw new Error(`Field "${field.label}" is required.`);
    }
  }

  let path = definition.path;
  if (values.sessionId) {
    path = path.replace('{sessionId}', encodeURIComponent(values.sessionId));
  }

  let payload;
  if (requestTypeSelect.value === 'create-session') {
    const usesLocalChatCompletionsUrl = isLocalChatCompletionsUrl(values.chatCompletionsUrl);

    payload = {};
    if (!usesLocalChatCompletionsUrl && values.model) payload.model = values.model;
    if (values.instructions) payload.instructions = values.instructions;
    if (values.chatCompletionsUrl) payload.chatCompletionsUrl = values.chatCompletionsUrl;
    if (!usesLocalChatCompletionsUrl && values.promptCacheKey) payload.promptCacheKey = values.promptCacheKey;
    if (!usesLocalChatCompletionsUrl && values.tier) payload.tier = values.tier;
    if (!usesLocalChatCompletionsUrl && values.reasoning) payload.reasoning = values.reasoning;
    if (!usesLocalChatCompletionsUrl && values.verbosity) payload.verbosity = values.verbosity;
  }

  if (requestTypeSelect.value === 'submit-message') {
    payload = { message: values.message };
  }

  const hasBody = definition.method !== 'GET';
  return {
    url: `${baseUrl}${path}`,
    method: definition.method,
    payload: hasBody ? payload : undefined
  };
}

async function sendRequest() {
  sendBtn.disabled = true;
  sendBtn.textContent = 'Sending...';
  setStatus('Sending request', 'idle');
  responseBody.textContent = '';

  try {
    const request = buildRequest();
    const result = await callApi(request.url, request.method, request.payload);

    if (result.ok) {
      setStatus(`${result.status} ${result.statusText}`, 'success');
    } else if (result.status >= 400 && result.status < 500) {
      setStatus(`${result.status} ${result.statusText}`, 'warn');
    } else {
      setStatus(`${result.status} ${result.statusText}`, 'error');
    }

    responseBody.textContent = formatIfJson(result.text) || '(empty response body)';
    let cachedInputTokens = getCachedInputTokens(result.text);

    // Sending a message returns only assistant text, so fetch the session snapshot to show cache status.
    if (requestTypeSelect.value === 'submit-message' && cachedInputTokens === null && result.ok) {
      const formValues = readFormValues();
      const sessionId = formValues.sessionId;

      if (sessionId) {
        const baseUrl = baseUrlInput.value.trim().replace(/\/$/, '');
        const sessionSnapshot = await callApi(`${baseUrl}/sessions/${encodeURIComponent(sessionId)}`, 'GET');

        if (sessionSnapshot.ok) {
          cachedInputTokens = getCachedInputTokens(sessionSnapshot.text);
        }
      }
    }

    // Keep the previous cache pill text when the response does not include cache data.
    if (cachedInputTokens === null) {
      // Do nothing so the previous cache status remains visible.
    } else if (cachedInputTokens > 0) {
      setCacheStatus(`Cached input tokens: ${cachedInputTokens}`, 'hit');
    } else {
      setCacheStatus('No cached input tokens', 'miss');
    }

    if (requestTypeSelect.value === 'create-session' && result.ok) {
      const createdSessionId = findSessionIdFromResponse(result.text);
      if (createdSessionId) {
        saveLastSessionId(createdSessionId);
      }
    }
  } catch (error) {
    setStatus('Request failed', 'error');
    responseBody.textContent = error instanceof Error ? error.message : String(error);
  } finally {
    sendBtn.disabled = false;
    sendBtn.textContent = 'Send request';
  }
}

function clearAll() {
  renderDynamicFields();
  setStatus('Waiting for request', 'idle');
  setCacheStatus('Cache status unavailable', 'unknown');
  responseBody.textContent = 'Send a request to see the response here.';
}

requestTypeSelect.addEventListener('change', renderDynamicFields);
sendBtn.addEventListener('click', sendRequest);
clearBtn.addEventListener('click', clearAll);

// Copy the response payload so the user can quickly reuse IDs and values.
copyBtn.addEventListener('click', async () => {
  const text = responseBody.textContent;
  if (!text) return;

  try {
    await navigator.clipboard.writeText(text);
    copyBtn.textContent = 'Copied!';
    copyBtn.classList.add('copied');
    setTimeout(() => {
      copyBtn.textContent = 'Copy';
      copyBtn.classList.remove('copied');
    }, 2000);
  } catch {
    copyBtn.textContent = 'Failed';
    setTimeout(() => {
      copyBtn.textContent = 'Copy';
    }, 2000);
  }
});

// Initial rendering makes sure the form matches the default request type.
renderDynamicFields();
