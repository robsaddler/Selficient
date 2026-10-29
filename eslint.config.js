import js from "@eslint/js";

export default [
  js.configs.recommended,
  {
    files: ["src/Selficient.Web/wwwroot/js/**/*.js"],
    languageOptions: {
      ecmaVersion: 2024,
      sourceType: "module",
      globals: {
        window: "readonly",
        document: "readonly",
        customElements: "readonly",
        HTMLElement: "readonly",
        HTMLButtonElement: "readonly",
        HTMLDialogElement: "readonly",
        localStorage: "readonly",
        matchMedia: "readonly",
        requestAnimationFrame: "readonly",
        performance: "readonly",
        AbortController: "readonly",
        Intl: "readonly",
      },
    },
    rules: {
      "no-var": "error",
      "prefer-const": "error",
      eqeqeq: ["error", "always"],
      "no-implicit-globals": "error",
    },
  },
  {
    files: ["build/**/*.mjs"],
    languageOptions: {
      ecmaVersion: 2024,
      sourceType: "module",
      globals: { process: "readonly", console: "readonly" },
    },
  },
];
