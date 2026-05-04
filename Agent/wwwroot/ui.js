const requestDefinitions = {
  'create-session': {
    method: 'POST',
    path: '/sessions',
    fields: [
      { key: 'instructions', label: 'Instructions', type: 'textarea', placeholder: 'You are a helpful assistant.', defaultValue: 'You are a helpful assistant.' }
    ]
  },
  'submit-message': {
    method: 'POST',
    path: '/sessions/{sessionId}/messages',
    fields: [
      { key: 'sessionId', label: 'Session ID', type: 'text', placeholder: 'GUID', required: true },
      { key: 'model', label: 'Request Profile', type: 'select', options: ['OpenAi', 'GptOss', 'Qwen36'], defaultValue: 'GptOss' },
      { key: 'chatCompletionsUrl', label: 'Chat Completions URL', type: 'text', defaultValue: 'https://api.openai.com/v1/chat/completions', readOnly: true },
      { key: 'message', label: 'Message', type: 'textarea', defaultValue: 'Hi', required: true },
      { key: 'maxIterations', label: 'Max Iterations', type: 'text', placeholder: '5', defaultValue: '5' },
      { key: 'toolkit', label: 'Toolkit', type: 'text', placeholder: 'Example' },
      { key: 'modelName', label: 'Model Name', type: 'text', defaultValue: 'gpt-5-nano' },
      { key: 'promptCacheKey', label: 'Prompt Cache Key', type: 'text', placeholder: 'SharpAgentHarness', defaultValue: 'SharpAgentHarness' },
      {
        key: 'serviceTier',
        label: 'Service Tier',
        type: 'select',
        options: ['Auto', 'Default', 'Flex', 'Scale', 'Priority'],
        defaultValue: 'Auto'
      },
      {
        key: 'reasoningEffort',
        label: 'Reasoning Effort',
        type: 'select',
        options: ['None', 'Minimal', 'Low', 'Medium', 'High', 'XHigh'],
        defaultValue: 'Minimal'
      },
      {
        key: 'gptOssReasoningEffort',
        label: 'Reasoning Effort',
        type: 'select',
        options: ['Low', 'Medium', 'High'],
        defaultValue: 'Low'
      },
      { key: 'enableThinking', label: 'Enable Thinking', type: 'select', options: ['true', 'false'], defaultValue: 'true' },
      {
        key: 'verbosity',
        label: 'Text Verbosity',
        type: 'select',
        options: ['Low', 'Medium', 'High'],
        defaultValue: 'Low'
      },
      { key: 'structuredOutputSectionTitle', label: 'Structured Output', type: 'section-title' },
      { key: 'structuredOutputEnabled', label: 'Enable Structured Output', type: 'select', options: ['false', 'true'], defaultValue: 'false' },
      { key: 'jsonSchemaName', label: 'Schema Name', type: 'text', placeholder: 'structured_response', note: "Used when structured output is enabled. Defaults to 'structured_response'." },
      { key: 'jsonStrict', label: 'Strict Schema', type: 'select', options: ['true', 'false'], defaultValue: 'true' },
      { key: 'jsonSchema', label: 'Schema JSON', type: 'textarea', placeholder: '{\n  \"type\": \"object\",\n  \"properties\": {\n    \"summary\": { \"type\": \"string\" }\n  },\n  \"required\": [\"summary\"],\n  \"additionalProperties\": false\n}' }
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

const openAiOnlyFieldKeys = ['modelName', 'promptCacheKey', 'serviceTier', 'reasoningEffort', 'verbosity'];
const modelChatCompletionsUrls = {
  OpenAi: 'https://api.openai.com/v1/chat/completions',
  GptOss: 'http://localhost:8080/chat/completions',
  Qwen36: 'http://localhost:8080/chat/completions'
};
const gptOssOnlyFieldKeys = ['gptOssReasoningEffort'];
const qwenOnlyFieldKeys = ['enableThinking'];
const structuredOutputFieldKeys = ['jsonSchemaName', 'jsonStrict', 'jsonSchema'];


// Keep track of the most recently created session so follow-up calls are quicker to fill in.
const LAST_SESSION_STORAGE_KEY = 'sharpAgentHarnessLastSession';
let lastCreatedSession = loadLastSession();

function loadLastSession() {
  try {
    const storedValue = localStorage.getItem(LAST_SESSION_STORAGE_KEY);

    if (!storedValue) {
      return null;
    }

    const parsed = JSON.parse(storedValue);
    if (!parsed || typeof parsed !== 'object') {
      return null;
    }

    const sessionId = typeof parsed.id === 'string' ? parsed.id : '';
    if (!sessionId) {
      return null;
    }

    return {
      id: sessionId
    };
  } catch {
    return null;
  }
}

function saveLastSession(session) {
  lastCreatedSession = session;

  try {
    localStorage.setItem(LAST_SESSION_STORAGE_KEY, JSON.stringify(session));
  } catch {
    // Ignore storage write errors, because session auto-fill is a convenience feature.
  }
}

function findSessionFromResponse(responseText) {
  try {
    const parsed = JSON.parse(responseText);

    if (parsed && typeof parsed === 'object' && typeof parsed.id === 'string') {
      return {
        id: parsed.id
      };
    }
  } catch {
    return null;
  }

  return null;
}

function populateSessionIdFieldIfAvailable() {
  if (!lastCreatedSession?.id) {
    return;
  }

  const sessionIdField = dynamicFields.querySelector('[data-field="sessionId"]');
  if (!sessionIdField) {
    return;
  }

  if (!sessionIdField.value.trim()) {
    sessionIdField.value = lastCreatedSession.id;
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

function updateRequestFieldVisibility() {
  const modelField = dynamicFields.querySelector('[data-field="model"]');
  const selectedModel = modelField?.value || 'OpenAi';

  openAiOnlyFieldKeys.forEach((fieldKey) => {
    const row = dynamicFields.querySelector(`[data-field-row="${fieldKey}"]`);
    if (row) row.classList.toggle('is-hidden', selectedModel !== 'OpenAi');
  });
  gptOssOnlyFieldKeys.forEach((fieldKey) => {
    const row = dynamicFields.querySelector(`[data-field-row="${fieldKey}"]`);
    if (row) row.classList.toggle('is-hidden', selectedModel !== 'GptOss');
  });
  qwenOnlyFieldKeys.forEach((fieldKey) => {
    const row = dynamicFields.querySelector(`[data-field-row="${fieldKey}"]`);
    if (row) row.classList.toggle('is-hidden', selectedModel !== 'Qwen36');
  });

  const modelUrlField = dynamicFields.querySelector('[data-field="chatCompletionsUrl"]');
  if (modelUrlField) {
    modelUrlField.value = modelChatCompletionsUrls[selectedModel] || '';
  }

  const structuredOutputEnabledField = dynamicFields.querySelector('[data-field="structuredOutputEnabled"]');
  const structuredOutputEnabled = structuredOutputEnabledField?.value === 'true';
  const structuredOutputSectionTitle = dynamicFields.querySelector('[data-field-row="structuredOutputSectionTitle"]');
  if (structuredOutputSectionTitle) {
    structuredOutputSectionTitle.classList.toggle('is-hidden', requestTypeSelect.value !== 'submit-message');
  }
  structuredOutputFieldKeys.forEach((fieldKey) => {
    const row = dynamicFields.querySelector(`[data-field-row="${fieldKey}"]`);
    if (row) row.classList.toggle('is-hidden', !structuredOutputEnabled);
  });
}

function renderDynamicFields() {
  const definition = requestDefinitions[requestTypeSelect.value];
  dynamicFields.innerHTML = '';

  definition.fields.forEach((field) => {
    if (field.type === 'section-title') {
      const heading = document.createElement('h3');
      heading.className = 'response-block-title';
      heading.textContent = field.label;
      heading.dataset.fieldRow = field.key;
      dynamicFields.appendChild(heading);
      return;
    }

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
    if (field.readOnly) {
      control.readOnly = true;
    }

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

  const sessionIdField = dynamicFields.querySelector('[data-field="sessionId"]');
  const modelField = dynamicFields.querySelector('[data-field="model"]');

  if (sessionIdField) {
    sessionIdField.addEventListener('input', updateRequestFieldVisibility);
    sessionIdField.addEventListener('change', updateRequestFieldVisibility);
  }
  if (modelField) {
    modelField.addEventListener('input', updateRequestFieldVisibility);
    modelField.addEventListener('change', updateRequestFieldVisibility);
  }

  populateSessionIdFieldIfAvailable();
  updateRequestFieldVisibility();
}

function readFormValues() {
  const values = {};
  dynamicFields.querySelectorAll('[data-field]').forEach((fieldElement) => {
    values[fieldElement.dataset.field] = fieldElement.value.trim();
  });
  return values;
}

function parseOptionalNumber(value, fieldLabel) {
  if (!value) {
    return undefined;
  }

  const parsedValue = Number(value);
  if (!Number.isFinite(parsedValue)) {
    throw new Error(`Field "${fieldLabel}" must be a valid number.`);
  }

  return parsedValue;
}

function parseOptionalInteger(value, fieldLabel) {
  if (!value) {
    return undefined;
  }

  const parsedValue = Number(value);
  if (!Number.isInteger(parsedValue)) {
    throw new Error(`Field "${fieldLabel}" must be a whole number.`);
  }

  return parsedValue;
}

function parseRequiredJson(value, fieldLabel) {
  if (!value.trim()) {
    throw new Error(`Field "${fieldLabel}" is required when structured output is enabled.`);
  }

  try {
    const parsedJson = JSON.parse(value);
    if (!parsedJson || typeof parsedJson !== 'object' || Array.isArray(parsedJson)) {
      throw new Error('Schema JSON must be a JSON object.');
    }

    return parsedJson;
  } catch (error) {
    const detail = error instanceof Error ? error.message : String(error);
    throw new Error(`Field "${fieldLabel}" must be valid JSON. ${detail}`);
  }
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
    payload = {};
    if (values.instructions) payload.instructions = values.instructions;
  }

  if (requestTypeSelect.value === 'submit-message') {
    const maxIterations = parseOptionalInteger(values.maxIterations, 'Max Iterations');
    const model = values.model || 'OpenAi';

    payload = { message: values.message, model };
    if (maxIterations !== undefined) payload.maxIterations = maxIterations;
    if (values.toolkit) payload.toolkit = values.toolkit;

    if (model === 'OpenAi') {
      if (values.modelName) payload.modelName = values.modelName;
      payload.openAi = {};
      if (values.promptCacheKey) payload.openAi.promptCacheKey = values.promptCacheKey;
      if (values.reasoningEffort) payload.openAi.reasoningEffort = values.reasoningEffort;
      if (values.verbosity) payload.openAi.verbosity = values.verbosity;
      if (values.serviceTier) payload.openAi.serviceTier = values.serviceTier;
    } else if (model === 'GptOss') {
      payload.gptOss = {};
      if (values.gptOssReasoningEffort) payload.gptOss.reasoningEffort = values.gptOssReasoningEffort;
    } else if (model === 'Qwen36') {
      payload.qwen = {
        enableThinking: values.enableThinking === 'true'
      };
    }

    if (values.structuredOutputEnabled === 'true') {
      payload.outputMode = 'json_schema';
      if (values.jsonSchemaName) payload.jsonSchemaName = values.jsonSchemaName;
      payload.jsonStrict = values.jsonStrict !== 'false';
      payload.jsonSchema = parseRequiredJson(values.jsonSchema, 'Schema JSON');
    }
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
      const createdSession = findSessionFromResponse(result.text);
      if (createdSession) {
        saveLastSession(createdSession);
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
