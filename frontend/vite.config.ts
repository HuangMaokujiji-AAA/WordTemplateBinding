/// <reference types="vitest/config" />
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

const apiOrigin = process.env.WTB_API_ORIGIN || "http://127.0.0.1:5080";

export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: fileURLToPath(
      new URL("../src/WordTemplateBinding.Api/wwwroot", import.meta.url)
    ),
    emptyOutDir: true,
  },
  server: {
    proxy: {
      "/api": apiOrigin,
    },
  },
  test: {
    environment: "jsdom",
    include: ["src/tests/**/*.test.ts"],
  },
});

