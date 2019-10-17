let config = {
  apiUrl: "__BAGET_PLACEHOLDER_API_URL__",
  baseUrl: "__BAGET_PATH_BASE_PLACEHOLDER__"
};

if (config.apiUrl.startsWith("__BAGET_PLACEHOLDER_")) {
  config.apiUrl = "";
}

if (config.baseUrl.startsWith("__BAGET_PLACEHOLDER_")) {
  config.baseUrl = "";
}

if (config.apiUrl.endsWith('/')) {
  config.apiUrl = config.apiUrl.slice(0, -1);
}

if (config.baseUrl.endsWith('/')) {
  config.baseUrl = config.baseUrl.slice(0, -1);
}

export { config };
