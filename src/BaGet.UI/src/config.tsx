let config = {
  apiUrl: "__BAGET_PLACEHOLDER_API_URL__"
};

// When runing `npm test` react-script automaticaly set this env variable
//   so we can test fetch request. (node fetch requires a full URL)
if (process.env.NODE_ENV === 'test' && config.apiUrl.startsWith("__BAGET_PLACEHOLDER_")) {
  config.apiUrl = 'http://localhost';
}

if (config.apiUrl.startsWith("__BAGET_PLACEHOLDER_")) {
  config.apiUrl = "";
}

if (config.apiUrl.endsWith('/')) {
  config.apiUrl = config.apiUrl.slice(0, -1);
}

export { config };
