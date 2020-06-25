let config = {
  apiUrl: "__BAGET_PLACEHOLDER_API_URL__"
};

if (process.env.NODE_ENV === 'test') {
  config.apiUrl = 'http://localhost';
}

if (config.apiUrl.startsWith("__BAGET_PLACEHOLDER_")) {
  config.apiUrl = "";
}

if (config.apiUrl.endsWith('/')) {
  config.apiUrl = config.apiUrl.slice(0, -1);
}

export { config };
